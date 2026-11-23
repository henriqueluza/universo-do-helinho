using System.Text.Json;

namespace Helinho.Application.Abstractions;

/// <summary>
/// Tratador de um ou mais tipos de trabalho (ADR-0003 §6.6). Roda em escopo próprio por item, sem transação aberta,
/// e <b>não salva</b> o resultado final: o que deixar no contexto é gravado pelo processador junto com a conclusão.
/// </summary>
public interface IJobHandler
{
    IReadOnlyCollection<string> Types { get; }

    JobPolicy Policy { get; }

    /// <summary>O token é cancelado quando o tempo máximo do item se esgota; repasse-o a toda chamada de I/O.</summary>
    Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken);
}

/// <summary>Gancho opcional (§6.7): chamado quando a fila encerra o trabalho por conta própria (<c>expired</c> ou <c>max_attempts</c>).</summary>
public interface IAbandonedJobHandler
{
    Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken);
}

/// <summary>O trabalho entregue ao tratador. <paramref name="Attempts"/> já conta a tentativa em curso (1 na primeira).</summary>
public sealed record JobContext(
    Guid Id, string Type, Guid? SubjectId, JsonElement Payload, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>Política do tratador (§6.2). <c>BatchSize × ItemTimeout ≤ Lease</c> é conferido na subida pelo trabalhador.</summary>
public sealed record JobPolicy(
    int MaxAttempts, TimeSpan MinBackoff, TimeSpan MaxBackoff, int BatchSize, TimeSpan ItemTimeout, TimeSpan Lease);

/// <summary>Resultado do tratador. Os códigos de erro são de vocabulário fechado (<see cref="JobCode"/>), nunca mensagem livre.</summary>
public abstract record JobResult
{
    private JobResult()
    {
    }

    public sealed record Succeeded(string? ExternalId = null) : JobResult;

    /// <summary>Erro transitório: nova tentativa dentro da política, não antes de <paramref name="NotBefore"/>.</summary>
    public sealed record Retry(string ErrorCode, DateTimeOffset? NotBefore = null) : JobResult
    {
        public string ErrorCode { get; } = JobCode.Require(ErrorCode, nameof(ErrorCode));
    }

    /// <summary>Erro permanente: vai direto para <c>Failed</c>.</summary>
    public sealed record Fail(string ErrorCode) : JobResult
    {
        public string ErrorCode { get; } = JobCode.Require(ErrorCode, nameof(ErrorCode));
    }

    /// <summary>Problema da conta no provedor (§6.3): volta a pendente sem gastar tentativa e pausa o laço do tratador.</summary>
    public sealed record Suspend(string ErrorCode, TimeSpan PauseFor) : JobResult
    {
        public string ErrorCode { get; } = JobCode.Require(ErrorCode, nameof(ErrorCode));
    }
}
