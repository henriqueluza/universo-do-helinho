using Helinho.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Persistence;

#pragma warning disable CA1711 // nome do ADR-0003 §5.5: é a fila de trabalhos, não uma coleção
/// <summary>Enfileira e cancela no <see cref="AppDbContext"/> de quem chama, sem salvar (ADR-0003 §5.5).</summary>
public sealed class JobQueue(AppDbContext db, IClock clock) : IJobQueue
{
    public async Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
    {
        if (request.IdempotencyKey is { } key)
        {
            // Local cobre o que este contexto ainda não salvou. A consulta devolve a instância rastreada:
            // se o trabalho foi cancelado aqui, ela já está sem a chave e o reenvio passa.
            var existing = db.QueueJobs.Local.FirstOrDefault(j => j.IdempotencyKey == key)
                ?? await db.QueueJobs.FirstOrDefaultAsync(j => j.IdempotencyKey == key, cancellationToken);
            if (existing?.IdempotencyKey == key)
            {
                return null;
            }
        }

        var job = new QueueJob(request, clock.UtcNow);
        db.QueueJobs.Add(job);
        return job.Id;
    }

    public async Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var found = await db.QueueJobs
            .Where(j => j.Type == type && j.SubjectId == subjectId && j.Status == QueueJobStatus.Pending)
            .ToListAsync(cancellationToken);
        // Refiltra em memória: uma instância já cancelada neste contexto ainda está pendente no banco.
        var pending = found.Where(j => j.Status == QueueJobStatus.Pending).ToList();
        // Cancel libera a chave de idempotência: o reenvio na mesma gravação passa pela conferência de EnqueueAsync (§5.4).
        pending.ForEach(j => j.Cancel(now));
        return pending.Count;
    }
}
#pragma warning restore CA1711
