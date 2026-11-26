using System.Text.Json;
using Helinho.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.Persistence;

/// <summary>
/// Processador genérico da fila (ADR-0003 §6): não conhece e-mail nem IA, só roteia pelo <c>Type</c> para o
/// <see cref="IJobHandler"/> registrado. A hora vem do <see cref="TimeProvider"/>, o mesmo relógio que grava
/// <c>NextAttemptAt</c> ao enfileirar.
/// </summary>
public sealed partial class JobProcessor(IServiceScopeFactory scopes, TimeProvider time, ILogger<JobProcessor> logger) : IJobProcessor
{
    public async Task<JobBatchResult> ProcessBatchAsync(JobLane lane, CancellationToken stopping, CancellationToken abort)
    {
        var jobs = await CaptureAsync(lane, stopping);
        JobResult.Suspend? suspended = null;
        foreach (var job in jobs)
        {
            // Depois de um suspender, o resto do lote é devolvido do mesmo jeito, sem chamar o provedor de novo.
            var result = await ProcessItemAsync(job, lane.Policy, suspended, abort);
            suspended ??= result;
        }

        return new(jobs.Count, suspended is null ? null : time.GetUtcNow() + suspended.PauseFor);
    }

    /// <summary>§6.1: um comando só. O estado é filtrado no próprio comando e a tentativa é contada ao pegar.</summary>
    private async Task<List<QueueJob>> CaptureAsync(JobLane lane, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var now = time.GetUtcNow();
        var (lease, types, batch) = (now + lane.Policy.Lease, lane.Types.ToArray(), lane.Policy.BatchSize);
        var jobs = await scope.ServiceProvider.GetRequiredService<AppDbContext>().QueueJobs.FromSql($"""
            UPDATE "QueueJobs" SET "Status" = 'Processing', "Attempts" = "Attempts" + 1, "NextAttemptAt" = {lease}
             WHERE "Id" IN (SELECT "Id" FROM "QueueJobs"
                             WHERE "Type" = ANY({types}) AND "Status" IN ('Pending', 'Processing') AND "NextAttemptAt" <= {now}
                             ORDER BY "NextAttemptAt" LIMIT {batch} FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        return [.. jobs.OrderBy(j => j.CreatedAt)];
    }

    /// <summary>Devolve o suspender do item, para o resto do lote e para a pausa da raia (§6.3).</summary>
    private async Task<JobResult.Suspend?> ProcessItemAsync(QueueJob job, JobPolicy policy, JobResult.Suspend? suspended, CancellationToken abort)
    {
        var started = time.GetTimestamp();
        await using var scope = scopes.CreateAsyncScope(); // §6.6: escopo próprio por item
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetServices<IJobHandler>().First(h => h.Types.Contains(job.Type));
        using var payload = JsonDocument.Parse(job.Payload);
        var context = new JobContext(job.Id, job.Type, job.SubjectId, payload.RootElement, job.Attempts, job.CreatedAt, job.ExpiresAt);

        // Nenhuma transação aberta aqui: a chamada externa do tratador acontece antes da gravação única.
        JobResult? result = suspended;
        var transition = JobTransition.WhenCaptured(job.Attempts, job.ExpiresAt, policy, time.GetUtcNow());
        if (transition is null)
        {
            result ??= await handler.HandleAsync(context, abort);
            var jitter = ((Random.Shared.NextDouble() * 2) - 1) * JobTransition.Jitter;
            transition = JobTransition.After(result, job.Attempts, job.ExpiresAt, policy, time.GetUtcNow(), jitter);
        }

        var outcome = transition switch
        {
            { Status: QueueJobStatus.Succeeded } => "succeeded",
            { Status: QueueJobStatus.Failed } => "failed",
            _ => transition.ReturnsAttempt ? "suspended" : "retry",
        };
        try
        {
            // Gravação final única (§6.6): resultado do tratador e conclusão no mesmo SaveChanges. Status e Attempts são
            // tokens de concorrência, então o UPDATE leva a guarda de posse. Nunca é repetida (§6.3): se falhar, o item
            // fica como está e reaparece pelo prazo de posse.
            db.Attach(job);
            job.Apply(transition, time.GetUtcNow());
            await db.SaveChangesAsync(abort);
        }
        catch (DbUpdateConcurrencyException)
        {
            outcome = "lease_lost";
        }
        catch (Exception exception) when (!abort.IsCancellationRequested)
        {
            outcome = "write_failed";
            Threw(job.Id, job.Type, context.Attempts, exception.GetType().Name); // só o tipo: a mensagem pode trazer SQL ou dado
        }

        var level = outcome switch { "succeeded" or "retry" => LogLevel.Information, "suspended" => LogLevel.Error, _ => LogLevel.Warning };
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        Finished(level, job.Id, job.Type, context.Attempts, outcome, transition.Error ?? "-", elapsedMs);
        return result as JobResult.Suspend;
    }

    [LoggerMessage(Message = "Fila: trabalho {JobId} ({JobType}), tentativa {Attempt}: {Outcome}, código {Code}, {ElapsedMs} ms.")]
    private partial void Finished(LogLevel level, Guid jobId, string jobType, int attempt, string outcome, string code, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fila: trabalho {JobId} ({JobType}), tentativa {Attempt}: exceção {ExceptionType}.")]
    private partial void Threw(Guid jobId, string jobType, int attempt, string exceptionType);
}
