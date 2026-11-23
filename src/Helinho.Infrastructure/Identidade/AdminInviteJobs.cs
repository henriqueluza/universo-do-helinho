using Helinho.Application.Administradores;
using Helinho.Application.Email;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Convites na fila, lidos pelo índice <c>IX_QueueJobs_Type_SubjectId_CreatedAt</c> (ADR-0003 §5.1). Só leitura.</summary>
public sealed class AdminInviteJobs(AppDbContext db) : IAdminInviteJobs
{
    public async Task<IReadOnlyDictionary<Guid, InviteJob>> LatestAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        var ids = accountIds.Cast<Guid?>().ToList();
        // Cancelado nunca foi enviado: não conta. São poucas linhas por conta (limite por hora, limpeza em 30 dias): o mais recente sai em memória.
        var jobs = await db.QueueJobs.AsNoTracking()
            .Where(j => j.Type == EmailJobTypes.AdminInvite && ids.Contains(j.SubjectId) && j.Status != QueueJobStatus.Cancelled)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new { j.SubjectId, j.Status, j.FinishedAt })
            .ToListAsync(cancellationToken);
        return jobs.GroupBy(j => j.SubjectId.GetValueOrDefault()).Select(g => (g.Key, Last: g.Last())).ToDictionary(
            g => g.Key,
            g => new InviteJob(g.Last.Status == QueueJobStatus.Failed, g.Last.Status == QueueJobStatus.Succeeded ? g.Last.FinishedAt : null));
    }

    public Task<int> CountSinceAsync(Guid accountId, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.QueueJobs.CountAsync(j => j.Type == EmailJobTypes.AdminInvite && j.SubjectId == accountId && j.CreatedAt >= since, cancellationToken);
}
