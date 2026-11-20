namespace Helinho.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>Uma tentativa de envio, sem repetição (repetir é de quem chama). Só lança quando o chamador cancela.</summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Mensagem imutável, com HTML e texto puro. <paramref name="Tag"/> é o tipo do envio, nunca dado pessoal;
/// <paramref name="IdempotencyKey"/> evita mensagem repetida quando a mesma tentativa é refeita.
/// </summary>
public sealed record EmailMessage(
    string Recipient, string Subject, string HtmlBody, string TextBody, string? Tag = null, Guid? IdempotencyKey = null)
{
    /// <summary>Sem destinatário, assunto nem corpo: a mensagem não vaza por log nem por interpolação.</summary>
    public override string ToString() => nameof(EmailMessage);
}

public enum EmailSendStatus
{
    /// <summary>Aceito pelo provedor.</summary>
    Sent,
    /// <summary>A mensagem foi recusada; repetir não muda o resultado.</summary>
    PermanentFailure,
    /// <summary>Limite de taxa, erro do provedor, tempo esgotado ou rede; vale repetir depois.</summary>
    TransientFailure,
    /// <summary>O problema é da conta no provedor (chave, validação, créditos), não da mensagem.</summary>
    ProviderSuspended,
}

/// <summary>
/// <paramref name="MessageId"/> é o id no provedor. <paramref name="ErrorCode"/> é um código curto de vocabulário fechado,
/// nunca texto do provedor; com <see cref="EmailSendStatus.Sent"/> e sem id, diz que o provedor já tinha aceitado a mesma
/// chave de idempotência. <paramref name="RetryAfter"/> é a espera mínima pedida pelo provedor.
/// </summary>
public sealed record EmailSendResult(
    EmailSendStatus Status, string? MessageId = null, string? ErrorCode = null, TimeSpan? RetryAfter = null);
