using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Envios contados na própria fila (ADR-0003 §15): trabalhos <c>email.*</c> terminados em <c>Succeeded</c>. É uma
/// contagem, sem carregar linha: o contexto do item não pode rastrear o trabalho em curso.
/// </summary>
public sealed class EmailDailyCount(AppDbContext db) : IEmailDailyCount
{
    public Task<int> SentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        db.QueueJobs.CountAsync(
            j => j.Type.StartsWith("email.") && j.Status == QueueJobStatus.Succeeded && j.FinishedAt >= since, cancellationToken);
}
