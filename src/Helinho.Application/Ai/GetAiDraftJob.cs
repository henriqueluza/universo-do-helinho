using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Application.Ai;

public enum GetAiDraftJobStatus
{
    Found,
    NotFound,
    Disabled,
    Forbidden,
}

/// <summary>Uma sugestão como a tela de revisão a mostra. O texto é gerado por IA: exibir sempre codificado, nunca como HTML.</summary>
public sealed record AiDraftView(
    Guid Id, int Position, string Statement, IReadOnlyList<string> Options, int CorrectIndex, string Explanation,
    AiSuggestionStatus Status, Guid? QuestionId);

/// <summary>Estado de um pedido em lote. <paramref name="FailureReason"/> é código (<see cref="AiFailureCodes"/>); a tela o traduz.</summary>
public sealed record AiDraftJobView(
    Guid Id, QuizType QuizType, AgeBand AgeBand, int RequestedCount, string? Subject, AiJobStatus Status, string? FailureReason,
    int GeneratedCount, int ValidCount, int InvalidCount, int DuplicateCount, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt,
    IReadOnlyList<AiDraftView> Drafts)
{
    /// <summary>"N de M revisadas": M é <see cref="Drafts"/>.Count.</summary>
    public int ReviewedCount => Drafts.Count(d => d.Status != AiSuggestionStatus.Pending);
}

public sealed record GetAiDraftJobResult(GetAiDraftJobStatus Status, AiDraftJobView? Job = null);

/// <summary>
/// Lê um pedido em lote para a tela (ADR-0004 §10.2): a fonte da verdade é <c>AiGenerationJobs</c>, não a fila.
/// Também é a conferência de pedido vencido, que cobre o caso de o gancho de encerramento da fila falhar.
/// </summary>
public sealed class GetAiDraftJob(IAppDbContext db, IClock clock, ICurrentUser user, IOptions<AiOptions> options)
{
    public async Task<GetAiDraftJobResult> HandleAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled)
        {
            return new GetAiDraftJobResult(GetAiDraftJobStatus.Disabled);
        }

        if (AiAccess.Reviewer(user) is null)
        {
            return new GetAiDraftJobResult(GetAiDraftJobStatus.Forbidden);
        }

        // Vencido = passou da validade do trabalho na fila mais o prazo da chamada: depois disso nenhum tratador vivo
        // ainda pode concluir. A atualização é condicional ao estado, então nunca desfaz uma conclusão do trabalhador.
        var now = clock.UtcNow.ToUniversalTime();
        var expiredBefore = now - RequestAiDrafts.ExpiresIn - TimeSpan.FromSeconds(settings.TimeoutSeconds);
        await db.AiGenerationJobs
            .Where(j => j.Id == jobId && (j.Status == AiJobStatus.Pending || j.Status == AiJobStatus.Processing) && j.CreatedAt < expiredBefore)
            .ExecuteUpdateAsync(
                set => set.SetProperty(j => j.Status, AiJobStatus.Failed).SetProperty(j => j.FailureReason, AiFailureCodes.Expired)
                    .SetProperty(j => j.FinishedAt, now),
                cancellationToken);

        var job = await db.AiGenerationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.Kind == AiJobKind.Batch, cancellationToken);
        if (job is null)
        {
            return new GetAiDraftJobResult(GetAiDraftJobStatus.NotFound);
        }

        var suggestions = await db.AiQuestionSuggestions.AsNoTracking().Where(s => s.JobId == jobId).OrderBy(s => s.Position).ToListAsync(cancellationToken);
        return new GetAiDraftJobResult(GetAiDraftJobStatus.Found, new AiDraftJobView(
            job.Id, job.QuizType, job.AgeBand, job.RequestedCount, job.Subject, job.Status, job.FailureReason,
            job.GeneratedCount, job.ValidCount, job.InvalidCount, job.DuplicateCount, job.CreatedAt, job.FinishedAt,
            [.. suggestions.Select(s => new AiDraftView(s.Id, s.Position, s.Statement, s.Options, s.CorrectIndex, s.Explanation, s.Status, s.QuestionId))]));
    }
}
