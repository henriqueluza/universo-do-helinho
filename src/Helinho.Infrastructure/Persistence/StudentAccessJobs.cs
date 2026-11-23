using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Lê os pedidos de acesso do aluno na fila pelo índice de tipo, sujeito e criação (ADR-0003 §5.1).</summary>
public sealed class StudentAccessJobs(AppDbContext db) : IStudentAccessJobs
{
    public async Task<StudentAccessHistory> ReadAsync(Guid studentId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var jobs = db.QueueJobs.AsNoTracking().Where(j => j.Type == EmailJobTypes.StudentAccess && j.SubjectId == studentId);
        var from = since.ToUniversalTime();
        var recent = await jobs.CountAsync(j => j.CreatedAt >= from && j.Status != QueueJobStatus.Cancelled, cancellationToken);
        var last = await jobs.OrderByDescending(j => j.CreatedAt)
            .Select(j => new { j.Status, j.CreatedAt, j.FinishedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            return new StudentAccessHistory(recent, null, null);
        }

        var status = last.Status switch
        {
            QueueJobStatus.Succeeded => DeliveryStatus.Sent,
            QueueJobStatus.Failed => DeliveryStatus.Failed,
            QueueJobStatus.Cancelled => DeliveryStatus.Cancelled,
            _ => DeliveryStatus.Queued,
        };
        return new StudentAccessHistory(recent, status, last.FinishedAt ?? last.CreatedAt);
    }
}
