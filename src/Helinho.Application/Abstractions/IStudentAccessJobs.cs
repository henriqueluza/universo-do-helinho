namespace Helinho.Application.Abstractions;

/// <summary>Situação de um envio na fila, como a ficha do aluno a mostra (ADR-0003 §5.1).</summary>
public enum DeliveryStatus
{
    Queued,
    Sent,
    Failed,
    Cancelled,
}

/// <summary>Pedidos de acesso de um aluno: quantos contam para o limite e a situação do mais recente (com a data dele).</summary>
public sealed record StudentAccessHistory(int RecentCount, DeliveryStatus? LastStatus, DateTimeOffset? LastAt);

/// <summary>Leitura da fila para o acesso do aluno (ADR-0003 §5.4 e §11.4). Quem enfileira e cancela é <see cref="IJobQueue"/>.</summary>
public interface IStudentAccessJobs
{
    /// <summary>Pedidos criados desde <paramref name="since"/> (cancelados não contam) e o mais recente de todos.</summary>
    Task<StudentAccessHistory> ReadAsync(Guid studentId, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Há aviso de troca do e-mail do responsável ainda por enviar (pendente ou em processamento) para o aluno?</summary>
    Task<bool> HasPendingGuardianNoticeAsync(Guid studentId, CancellationToken cancellationToken);
}
