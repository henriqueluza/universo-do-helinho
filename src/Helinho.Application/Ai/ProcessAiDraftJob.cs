using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Application.Ai;

/// <summary>
/// Códigos de falha de um pedido de IA (ADR-0004 §10.2): os mesmos em <c>AiGenerationJobs.FailureReason</c> e em
/// <c>QueueJobs.LastError</c>. Vocabulário fechado; a tela traduz o código em mensagem.
/// </summary>
public static class AiFailureCodes
{
    public const string Timeout = "ai_timeout";

    /// <summary>A fila encerrou o trabalho pela validade, ou a leitura encontrou o pedido vencido.</summary>
    public const string Expired = "expired";

    /// <summary>O trabalho não aponta para um pedido em lote que exista: defeito de quem enfileirou, não do provedor.</summary>
    public const string JobMissing = "ai_job_missing";

    public static string Of(QuestionDraftFailure reason) => reason switch
    {
        QuestionDraftFailure.Timeout => Timeout,
        QuestionDraftFailure.ProviderUnavailable => "ai_unavailable",
        QuestionDraftFailure.RateLimited => "ai_rate_limited",
        QuestionDraftFailure.Refused => "ai_refused",
        QuestionDraftFailure.InvalidOutput => "ai_invalid_output",
        QuestionDraftFailure.Misconfigured => "ai_misconfigured",
        QuestionDraftFailure.Disabled => "ai_disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}

/// <summary>
/// Tratador do trabalho <c>ai.question-drafts</c> (ADR-0004 §10.2). Chama o gerador fora de transação, valida a saída
/// como dado não confiável e deixa no contexto as sugestões, os contadores e o uso: quem grava é o processador, junto
/// com a conclusão do trabalho e com a guarda de posse (ADR-0003 §6.6). Só devolve sucesso ou falha; não escreve log.
/// </summary>
public sealed class ProcessAiDraftJob(IAppDbContext db, IQuestionDraftGenerator generator, IClock clock, IOptions<AiOptions> options)
    : IJobHandler, IAbandonedJobHandler
{
    /// <summary>A segunda tentativa só existe para o processo que morreu no meio ou para exceção inesperada.</summary>
    public const int MaxAttempts = 2;

    /// <summary>Folga da posse sobre o prazo da chamada: um tratador lento mas vivo não é capturado de novo.</summary>
    public static readonly TimeSpan LeaseMargin = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(10);

    public IReadOnlyCollection<string> Types { get; } = [RequestAiDrafts.JobType];

    public JobPolicy Policy
    {
        get
        {
            var itemTimeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
            return new JobPolicy(MaxAttempts, Backoff, Backoff, BatchSize: 1, itemTimeout, itemTimeout + LeaseMargin);
        }
    }

    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        if (await FindRequestAsync(job, cancellationToken) is not { } request)
        {
            return new JobResult.Fail(AiFailureCodes.JobMissing);
        }

        if (request.IsFinished)
        {
            return new JobResult.Succeeded(); // trabalho capturado de novo: não paga outra chamada
        }

        if (request.Status == AiJobStatus.Pending)
        {
            // Gravação intermediária (§6.6): tira a tela de "na fila". Repetir não tem efeito.
            request.Start(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            // Ao provedor só vão tipo, faixa, quantidade, assunto e enunciados de questões do mesmo tipo e faixa (§11).
            var questions = await QuestionDraftValidator.ListExistingStatementsAsync(db, request.QuizType, request.AgeBand, cancellationToken);
            var batch = await generator.GenerateAsync(
                QuestionDraftRequest.Create(request.QuizType, request.AgeBand, request.RequestedCount, request.Subject, questions), cancellationToken);

            var stored = await QuestionDraftValidator.ListStoredSuggestionStatementsAsync(db, request.QuizType, request.AgeBand, cancellationToken);
            var validation = QuestionDraftValidator.Validate(batch.Drafts, questions.Concat(stored), request.RequestedCount);
            db.AiQuestionSuggestions.AddRange(validation.Accepted.Select((draft, position) =>
                new AiQuestionSuggestion(request.Id, position, draft.Statement, draft.Options, draft.CorrectIndex, draft.Explanation)));
            request.Succeed(
                new AiJobUsage(batch.Usage.Provider, batch.Usage.Model, batch.PromptVersion, batch.Usage.InputTokens, batch.Usage.OutputTokens, batch.Usage.Duration),
                new AiJobCounts(batch.Drafts.Count, validation.Accepted.Count, validation.InvalidCount, validation.DuplicateCount),
                clock.UtcNow);
            return new JobResult.Succeeded(SafeExternalId(batch.Usage.ProviderRequestId));
        }
        catch (QuestionDraftGenerationException failure)
        {
            return Fail(request, AiFailureCodes.Of(failure.Reason));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(request, AiFailureCodes.Timeout); // o processador cancela o token no tempo máximo do item
        }
    }

    /// <summary>A fila encerrou o trabalho por conta própria (<c>expired</c> ou <c>max_attempts</c>): tira o pedido de "gerando".</summary>
    public async Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken)
    {
        if (await FindRequestAsync(job, cancellationToken) is { IsFinished: false } request)
        {
            request.Fail(reason, clock.UtcNow);
        }
    }

    private JobResult.Fail Fail(AiGenerationJob request, string code)
    {
        request.Fail(code, clock.UtcNow);
        return new JobResult.Fail(code);
    }

    private async Task<AiGenerationJob?> FindRequestAsync(JobContext job, CancellationToken cancellationToken) =>
        job.Payload.ValueKind == JsonValueKind.Object && job.Payload.TryGetProperty(RequestAiDrafts.PayloadJobId, out var value)
        && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)
            ? await db.AiGenerationJobs.FirstOrDefaultAsync(j => j.Id == id && j.Kind == AiJobKind.Batch, cancellationToken)
            : null;

    /// <summary>O <c>request-id</c> vem de um cabeçalho do provedor: só segue para a fila se tiver cara de identificador.</summary>
    private static string? SafeExternalId(string? id) =>
        id is { Length: > 0 and <= 200 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? id : null;
}
