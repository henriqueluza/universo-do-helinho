namespace Helinho.Application.Email;

/// <summary>
/// Retorno posterior do provedor sobre um e-mail já enviado (ADR-0003 §12): "não entregue". É aviso de melhor esforço
/// para as telas, não registro oficial de entrega.
/// </summary>
public interface IEmailFeedbackRecorder
{
    /// <summary>
    /// Grava o retorno no trabalho de e-mail cujo id no provedor é <paramref name="messageId"/> e devolve o id do trabalho.
    /// Devolve <c>null</c>, sem mudar nada, quando o evento não está em <see cref="EmailFeedback"/>, o id é desconhecido
    /// ou o trabalho já tem retorno: o primeiro vale, então repetir o evento dá o mesmo estado.
    /// </summary>
    Task<Guid?> RecordAsync(string providerEvent, string messageId, CancellationToken cancellationToken);
}

/// <summary>Os quatro retornos que entram (ADR-0003 §12, D18). O código gravado em <c>Feedback</c> é o nome do evento.</summary>
public static class EmailFeedback
{
    public const string HardBounce = "hard_bounce";
    public const string InvalidEmail = "invalid_email";
    public const string Blocked = "blocked";
    public const string Spam = "spam";

    /// <summary>Tamanho da coluna <c>ExternalId</c> (§5.1).</summary>
    private const int MaxMessageIdLength = 200;

    public static bool IsKnown(string? providerEvent) => providerEvent is HardBounce or InvalidEmail or Blocked or Spam;

    /// <summary>O id é guardado e comparado sem <c>&lt;</c> e <c>&gt;</c> (§12). Vazio ou longo demais não é id de ninguém.</summary>
    public static string? NormalizeMessageId(string? messageId) =>
        messageId?.Trim().TrimStart('<').TrimEnd('>') is { Length: > 0 and <= MaxMessageIdLength } id ? id : null;
}
