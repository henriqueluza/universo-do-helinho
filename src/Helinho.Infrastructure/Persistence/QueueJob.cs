using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Estados do trabalho (ADR-0003 §5.1), gravados como texto.</summary>
public enum QueueJobStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// Linha da fila persistente (ADR-0003 §5.1). Só referências: o destinatário é resolvido, o token gerado e a mensagem
/// renderizada pelo tratador na hora de enviar.
/// </summary>
public class QueueJob
{
    private QueueJob()
    {
    }

    public QueueJob(JobRequest request, DateTimeOffset now)
    {
        Type = request.Type;
        SubjectId = request.SubjectId;
        Payload = request.Payload.ToJson();
        IdempotencyKey = request.IdempotencyKey;
        CreatedAt = NextAttemptAt = now.ToUniversalTime();
        ExpiresAt = CreatedAt + request.ExpiresIn;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Type { get; private set; } = "";

    /// <summary>A quem o trabalho se refere (usuário ou aluno). Sem chave estrangeira.</summary>
    public Guid? SubjectId { get; private set; }

    public string Payload { get; private set; } = "";

    public QueueJobStatus Status { get; private set; } = QueueJobStatus.Pending;

    /// <summary>Tentativas já iniciadas. Com <see cref="Status"/>, é token de concorrência: a guarda de posse (§6.6).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string? LastError { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public string? ExternalId { get; private set; }

    public string? Feedback { get; private set; }

    public DateTimeOffset? FeedbackAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Só trabalho pendente é cancelado; a chave de idempotência é liberada para um reenvio na mesma janela.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != QueueJobStatus.Pending)
        {
            throw new InvalidOperationException("Só um trabalho pendente pode ser cancelado.");
        }

        Status = QueueJobStatus.Cancelled;
        FinishedAt = now.ToUniversalTime();
        IdempotencyKey = null;
    }

    /// <summary>
    /// Saída de <c>Processing</c>, na gravação final única do processador (§6.6). Suspender devolve a tentativa (§6.3):
    /// é o único caminho que diminui o contador.
    /// </summary>
    public void Apply(JobTransition transition, DateTimeOffset now)
    {
        if (Status != QueueJobStatus.Processing)
        {
            throw new InvalidOperationException("Só um trabalho em processamento recebe o resultado.");
        }

        Status = transition.Status;
        LastError = transition.Error;
        ExternalId = transition.ExternalId ?? ExternalId;
        Attempts -= transition.ReturnsAttempt ? 1 : 0;
        NextAttemptAt = transition.NextAttemptAt?.ToUniversalTime() ?? NextAttemptAt;
        FinishedAt = Status == QueueJobStatus.Pending ? null : now.ToUniversalTime();
    }
}
