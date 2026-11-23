using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Persistence;

/// <summary>
/// Como um trabalho capturado sai de <c>Processing</c> (ADR-0003 §6.2, §6.3 e §6.7). Função pura: só decide; quem grava
/// é o processador, na gravação final única. <paramref name="Abandoned"/> marca o encerramento decidido pela fila.
/// </summary>
public sealed record JobTransition(
    QueueJobStatus Status, string? Error = null, DateTimeOffset? NextAttemptAt = null, string? ExternalId = null,
    bool ReturnsAttempt = false, bool Abandoned = false)
{
    public const string Expired = "expired";
    public const string MaxAttempts = "max_attempts";
    public const string HandlerException = "handler_exception";

    /// <summary>Variação máxima da espera, para os dois lados (§6.2).</summary>
    public const double Jitter = 0.2;

    /// <summary>Visto ao pegar o item: passou da validade, ou é uma tentativa além do teto (o processo anterior morreu).</summary>
    public static JobTransition? WhenCaptured(int attempts, DateTimeOffset expiresAt, JobPolicy policy, DateTimeOffset now) =>
        now >= expiresAt ? Abandon(Expired) : attempts > policy.MaxAttempts ? Abandon(MaxAttempts) : null;

    /// <summary><paramref name="jitter"/> entre <c>-Jitter</c> e <c>+Jitter</c>; só entra na espera do repetir.</summary>
    public static JobTransition After(
        JobResult result, int attempts, DateTimeOffset expiresAt, JobPolicy policy, DateTimeOffset now, double jitter) => result switch
        {
            JobResult.Succeeded succeeded => new(QueueJobStatus.Succeeded, ExternalId: succeeded.ExternalId),
            JobResult.Fail fail => new(QueueJobStatus.Failed, fail.ErrorCode),
            // §6.3: devolve a tentativa. Não confere a validade: o trabalho só expira ao ser pego de novo.
            JobResult.Suspend suspend => new(QueueJobStatus.Pending, suspend.ErrorCode, now + suspend.PauseFor, ReturnsAttempt: true),
            JobResult.Retry when attempts >= policy.MaxAttempts => Abandon(MaxAttempts),
            JobResult.Retry retry => Retry(retry, attempts, expiresAt, policy, now, jitter),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    private static JobTransition Retry(
        JobResult.Retry retry, int attempts, DateTimeOffset expiresAt, JobPolicy policy, DateTimeOffset now, double jitter)
    {
        var wait = Math.Min(policy.MinBackoff.TotalSeconds * Math.Pow(2, attempts - 1), policy.MaxBackoff.TotalSeconds);
        var next = now + TimeSpan.FromSeconds(wait * (1 + Math.Clamp(jitter, -Jitter, Jitter)));
        if (retry.NotBefore > next)
        {
            next = retry.NotBefore.Value;
        }

        // Se a próxima espera cairia depois da validade, falha na hora em vez de esperar para expirar.
        return next > expiresAt ? Abandon(Expired) : new(QueueJobStatus.Pending, retry.ErrorCode, next);
    }

    private static JobTransition Abandon(string reason) => new(QueueJobStatus.Failed, reason, Abandoned: true);
}
