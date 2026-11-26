using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Persistence;

/// <summary>
/// Grava <c>Feedback</c> e <c>FeedbackAt</c> em <c>QueueJobs</c>, localizando o trabalho por <c>ExternalId</c> (ADR-0003 §12).
/// Atualização direta no banco, fora do rastreamento: não disputa o token de concorrência com o trabalhador.
/// </summary>
public sealed class EmailFeedbackRecorder(AppDbContext db, IClock clock) : IEmailFeedbackRecorder
{
    /// <summary>Só trabalho de e-mail recebe retorno: o <c>ExternalId</c> de outros tipos é id de outro provedor.</summary>
    private const string EmailTypes = "email.%";

    public async Task<Guid?> RecordAsync(string providerEvent, string messageId, CancellationToken cancellationToken)
    {
        if (!EmailFeedback.IsKnown(providerEvent) || EmailFeedback.NormalizeMessageId(messageId) is not { } externalId)
        {
            return null;
        }

        var jobId = await db.QueueJobs
            .Where(j => j.ExternalId == externalId && j.Feedback == null && EF.Functions.Like(j.Type, EmailTypes))
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (jobId is null)
        {
            return null;
        }

        // "Feedback == null" de novo: de duas chamadas simultâneas com o mesmo evento, só uma grava.
        var now = clock.UtcNow;
        var changed = await db.QueueJobs
            .Where(j => j.Id == jobId && j.Feedback == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Feedback, providerEvent).SetProperty(j => j.FeedbackAt, now), cancellationToken);
        return changed == 1 ? jobId : null;
    }
}
