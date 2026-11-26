using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Ai;

/// <param name="Count">Sugestões ainda pendentes, com o texto guardado.</param>
/// <param name="OldestJobId">O pedido mais antigo que ainda tem sugestão pendente: é por ele que a revisão recomeça.</param>
public sealed record PendingAiDraftsSummary(int Count, Guid? OldestJobId)
{
    public static readonly PendingAiDraftsSummary None = new(0, null);
}

/// <summary>
/// Quantas sugestões da IA esperam revisão (ADR-0004 §15, item 5): o aviso da lista de Questões. As sugestões são
/// globais, como o banco de questões (§9.3), então a contagem não é por pessoa. Com a IA desligada não há aviso.
/// </summary>
public static class PendingAiDrafts
{
    public static async Task<PendingAiDraftsSummary> CountAsync(IAppDbContext db, AiOptions options, CancellationToken cancellationToken)
    {
        if (!options.IsEnabled)
        {
            return PendingAiDraftsSummary.None;
        }

        var pending = db.AiQuestionSuggestions.AsNoTracking().Where(s => s.Status == AiSuggestionStatus.Pending && s.ContentPurgedAt == null);
        var count = await pending.CountAsync(cancellationToken);
        if (count == 0)
        {
            return PendingAiDraftsSummary.None;
        }

        var oldest = await db.AiGenerationJobs.AsNoTracking().Where(j => pending.Any(s => s.JobId == j.Id))
            .OrderBy(j => j.CreatedAt).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(cancellationToken);
        return new PendingAiDraftsSummary(count, oldest);
    }
}
