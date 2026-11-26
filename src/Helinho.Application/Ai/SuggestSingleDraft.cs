using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Application.Ai;

/// <summary>
/// <see cref="Rejected"/>: a IA respondeu, mas o validador descartou a sugestão (inválida ou duplicada).
/// <see cref="Failed"/>: a geração falhou; <see cref="SuggestSingleDraftResult.FailureReason"/> traz o código.
/// </summary>
public enum SuggestSingleDraftStatus
{
    Suggested,
    Rejected,
    Failed,
    QuotaExceeded,
    Disabled,
    Forbidden,
}

/// <param name="Draft">Texto gerado por IA, já validado: preenche o formulário e é exibido sempre codificado. Não foi gravado.</param>
/// <param name="JobId">Vai no campo oculto do formulário; ao salvar, é o que dá a origem IA à questão.</param>
public sealed record SuggestSingleDraftResult(
    SuggestSingleDraftStatus Status, Guid? JobId = null, QuestionDraft? Draft = null, string? FailureReason = null, AiQuotaExceeded? Quota = null);

/// <summary>
/// "Sugerir" no criador de questões (ADR-0004 §9.3 e §15): uma sugestão, pedida de forma síncrona e com prazo curto.
/// O pedido é gravado para cota, uso e auditoria; o texto sugerido não é gravado em lugar nenhum e nada é salvo como questão.
/// </summary>
public sealed class SuggestSingleDraft(
    IAppDbContext db, IQuestionDraftGenerator generator, IClock clock, ICurrentUser user, IOptions<AiOptions> options)
{
    /// <summary>A pessoa está esperando com o formulário aberto: o prazo é o menor entre este e <c>Ai:TimeoutSeconds</c>.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(20);

    /// <summary>Por quanto tempo o pedido avulso ainda dá origem IA à questão salva ("recente", §9.3).</summary>
    public static readonly TimeSpan JobValidity = TimeSpan.FromHours(2);

    /// <summary>O prazo de um "Sugerir": o menor entre <c>Ai:TimeoutSeconds</c> e <see cref="MaxWait"/> (ADR-0004 §13).</summary>
    public static TimeSpan WaitFor(int timeoutSeconds)
    {
        var wait = TimeSpan.FromSeconds(timeoutSeconds);
        return wait < MaxWait ? wait : MaxWait;
    }

    /// <exception cref="DomainException">Tipo ou faixa indefinidos.</exception>
    public async Task<SuggestSingleDraftResult> HandleAsync(QuizType quizType, AgeBand ageBand, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled)
        {
            return new SuggestSingleDraftResult(SuggestSingleDraftStatus.Disabled);
        }

        if (AiAccess.Reviewer(user) is not { } userId)
        {
            return new SuggestSingleDraftResult(SuggestSingleDraftStatus.Forbidden);
        }

        var job = new AiGenerationJob(AiJobKind.Single, quizType, ageBand, 1, null, userId, clock.UtcNow);
        if (await AiQuota.CheckAsync(db, settings, userId, 1, job.CreatedAt, cancellationToken) is { } exceeded)
        {
            return new SuggestSingleDraftResult(SuggestSingleDraftStatus.QuotaExceeded, Quota: exceeded);
        }

        // Gravado antes da chamada: o pedido conta na cota mesmo que a geração falhe ou a pessoa feche a página.
        job.Start(job.CreatedAt);
        db.AiGenerationJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(WaitFor(settings.TimeoutSeconds));
        SuggestSingleDraftResult result;
        try
        {
            var questions = await QuestionDraftValidator.ListExistingStatementsAsync(db, quizType, ageBand, deadline.Token);
            var batch = await generator.GenerateAsync(QuestionDraftRequest.Create(quizType, ageBand, 1, null, questions), deadline.Token);
            var stored = await QuestionDraftValidator.ListStoredSuggestionStatementsAsync(db, quizType, ageBand, deadline.Token);
            var validation = QuestionDraftValidator.Validate(batch.Drafts, questions.Concat(stored), limit: 1);
            job.Succeed(
                new AiJobUsage(batch.Usage.Provider, batch.Usage.Model, batch.PromptVersion, batch.Usage.InputTokens, batch.Usage.OutputTokens, batch.Usage.Duration),
                new AiJobCounts(batch.Drafts.Count, validation.Accepted.Count, validation.InvalidCount, validation.DuplicateCount),
                clock.UtcNow);
            result = validation.Accepted is [var draft, ..]
                ? new SuggestSingleDraftResult(SuggestSingleDraftStatus.Suggested, job.Id, draft)
                : new SuggestSingleDraftResult(SuggestSingleDraftStatus.Rejected, job.Id);
        }
        catch (QuestionDraftGenerationException failure)
        {
            result = Fail(job, AiFailureCodes.Of(failure.Reason));
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            result = Fail(job, AiFailureCodes.Timeout);
        }

        // Sem o token de quem chamou: se a pessoa fechou a página, o pedido ainda sai de "gerando".
        await db.SaveChangesAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>
    /// Cria a questão nova do criador. Com o identificador de um pedido avulso do mesmo usuário, concluído com sugestão
    /// válida e recente, ela nasce com origem IA e a proveniência; sem ele, ou com valor que não serve, nasce da equipe.
    /// Vale mesmo com a IA desligada agora: a origem é um fato sobre o texto.
    /// </summary>
    /// <exception cref="DomainException">Dado inválido, pelas mesmas regras de <see cref="SaveQuestion"/>.</exception>
    public async Task<(Guid QuestionId, QuestionOrigin Origin)> SaveAsync(
        Guid? aiJobId, QuizType quizType, AgeBand ageBand, string? statement, string? explanation,
        IReadOnlyList<string?> options, int? correctIndex, CancellationToken cancellationToken)
    {
        var items = SaveQuestion.ValidatedOptions(quizType, ageBand, statement, explanation, options, correctIndex);
        var since = clock.UtcNow.ToUniversalTime() - JobValidity;
        var job = aiJobId is { } id && AiAccess.Reviewer(user) is { } userId
            ? await db.AiGenerationJobs.AsNoTracking().FirstOrDefaultAsync(
                j => j.Id == id && j.Kind == AiJobKind.Single && j.RequestedByUserId == userId && j.Status == AiJobStatus.Succeeded
                    && j.ValidCount > 0 && j.FinishedAt > since, cancellationToken)
            : null;
        var question = job is { PromptVersion: { } version, FinishedAt: { } generatedAt }
            ? Question.FromAi(quizType, ageBand, statement ?? "", explanation ?? "", items, job.Id, version, generatedAt)
            : new Question(quizType, ageBand, statement ?? "", explanation ?? "", items);
        db.Questions.Add(question);
        await db.SaveChangesAsync(cancellationToken);
        return (question.Id, question.Origin);
    }

    private SuggestSingleDraftResult Fail(AiGenerationJob job, string code)
    {
        job.Fail(code, clock.UtcNow);
        return new SuggestSingleDraftResult(SuggestSingleDraftStatus.Failed, job.Id, FailureReason: code);
    }
}
