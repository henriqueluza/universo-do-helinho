using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Application.Ai;

/// <summary>
/// <see cref="AlreadyReviewed"/>: outra decisão chegou antes (ou o texto já foi apagado pela retenção).
/// <see cref="Duplicate"/>: já existe questão do mesmo tipo e faixa com o mesmo enunciado; a pessoa edita ou descarta.
/// </summary>
public enum AiDraftDecisionStatus
{
    Done,
    NotFound,
    AlreadyReviewed,
    Duplicate,
    Disabled,
    Forbidden,
}

/// <param name="QuestionId">A questão criada, ao aprovar ou ao salvar a edição.</param>
public sealed record AiDraftDecision(AiDraftDecisionStatus Status, Guid? QuestionId = null);

/// <summary>
/// Revisão humana das sugestões (ADR-0004 §9.3): é o único caminho pelo qual texto gerado por IA vira
/// <see cref="Question"/>. Cada decisão é uma gravação só e só vale a partir de <see cref="AiSuggestionStatus.Pending"/>.
/// </summary>
public sealed class ReviewAiDrafts(IAppDbContext db, IClock clock, ICurrentUser user, IOptions<AiOptions> options)
{
    /// <summary>Cria a questão já aprovada, com a proveniência, e marca a sugestão como aprovada.</summary>
    public Task<AiDraftDecision> ApproveAsync(Guid suggestionId, CancellationToken cancellationToken) =>
        DecideAsync(suggestionId, async (suggestion, job, userId, now) =>
        {
            // §8: a checagem roda de novo, porque uma questão igual pode ter sido criada entre a geração e a revisão.
            var statement = QuestionDraftValidator.Normalize(suggestion.Statement);
            var existing = await QuestionDraftValidator.ListExistingStatementsAsync(db, job.QuizType, job.AgeBand, cancellationToken);
            if (existing.Any(other => QuestionDraftValidator.Normalize(other) == statement))
            {
                return new AiDraftDecision(AiDraftDecisionStatus.Duplicate);
            }

            var question = NewQuestion(job, job.QuizType, job.AgeBand, suggestion.Statement, suggestion.Explanation,
                [.. suggestion.Options.Select((label, index) => (label, index == suggestion.CorrectIndex))]);
            question.Approve();
            suggestion.Approve(userId, question.Id, now);
            return new AiDraftDecision(AiDraftDecisionStatus.Done, question.Id);
        }, cancellationToken);

    public Task<AiDraftDecision> DiscardAsync(Guid suggestionId, CancellationToken cancellationToken) =>
        DecideAsync(suggestionId, (suggestion, _, userId, now) =>
        {
            suggestion.Discard(userId, now);
            return Task.FromResult(new AiDraftDecision(AiDraftDecisionStatus.Done));
        }, cancellationToken);

    /// <summary>
    /// Salva o criador aberto a partir da sugestão: a questão nasce em rascunho, com origem IA, e a sugestão vira editada.
    /// Nada é gravado ao abrir o formulário; se a pessoa cancelar, a sugestão continua pendente.
    /// </summary>
    /// <exception cref="DomainException">Dado inválido, pelas mesmas regras de <see cref="SaveQuestion"/>.</exception>
    public Task<AiDraftDecision> SaveEditedAsync(
        Guid suggestionId, QuizType quizType, AgeBand ageBand, string? statement, string? explanation,
        IReadOnlyList<string?> options, int? correctIndex, CancellationToken cancellationToken) =>
        DecideAsync(suggestionId, (suggestion, job, userId, now) =>
        {
            var items = SaveQuestion.ValidatedOptions(quizType, ageBand, statement, explanation, options, correctIndex);
            var question = NewQuestion(job, quizType, ageBand, statement ?? "", explanation ?? "", items);
            suggestion.MarkEdited(userId, question.Id, now);
            return Task.FromResult(new AiDraftDecision(AiDraftDecisionStatus.Done, question.Id));
        }, cancellationToken);

    private Question NewQuestion(
        AiGenerationJob job, QuizType quizType, AgeBand ageBand, string statement, string explanation, IReadOnlyList<(string, bool)> items)
    {
        // A sugestão só existe em pedido concluído com sucesso, que tem versão do prompt e hora de conclusão.
        var question = Question.FromAi(quizType, ageBand, statement, explanation, items, job.Id,
            job.PromptVersion ?? throw new InvalidOperationException("Pedido de IA sem versão do prompt."),
            job.FinishedAt ?? throw new InvalidOperationException("Pedido de IA sem hora de conclusão."));
        db.Questions.Add(question);
        return question;
    }

    private async Task<AiDraftDecision> DecideAsync(
        Guid suggestionId, Func<AiQuestionSuggestion, AiGenerationJob, Guid, DateTimeOffset, Task<AiDraftDecision>> decide,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IsEnabled)
        {
            return new AiDraftDecision(AiDraftDecisionStatus.Disabled);
        }

        if (AiAccess.Reviewer(user) is not { } userId)
        {
            return new AiDraftDecision(AiDraftDecisionStatus.Forbidden);
        }

        if (await db.AiQuestionSuggestions.FirstOrDefaultAsync(s => s.Id == suggestionId, cancellationToken) is not { } suggestion)
        {
            return new AiDraftDecision(AiDraftDecisionStatus.NotFound);
        }

        if (suggestion.Status != AiSuggestionStatus.Pending || suggestion.ContentPurgedAt is not null)
        {
            return new AiDraftDecision(AiDraftDecisionStatus.AlreadyReviewed);
        }

        var job = await db.AiGenerationJobs.AsNoTracking().FirstAsync(j => j.Id == suggestion.JobId, cancellationToken);
        var decision = await decide(suggestion, job, userId, clock.UtcNow);
        if (decision.Status != AiDraftDecisionStatus.Done)
        {
            return decision;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return decision;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Duas pessoas decidiram ao mesmo tempo: o Status é token de concorrência, e a gravação recusada é inteira
            // (a questão desta decisão também não entra).
            return new AiDraftDecision(AiDraftDecisionStatus.AlreadyReviewed);
        }
    }
}
