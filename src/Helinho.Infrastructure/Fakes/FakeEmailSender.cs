using Helinho.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.Fakes;

/// <summary>
/// Não envia nada; registra no log só o fato do envio (sem destinatário nem corpo, por LGPD). Em <c>Development</c>
/// a mensagem fica na <see cref="DevEmailOutbox"/>, em memória, para ser lida no painel.
/// </summary>
public sealed partial class FakeEmailSender(ILogger<FakeEmailSender> logger, DevEmailOutbox outbox, IClock clock) : IEmailSender
{
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        outbox.Add(message, clock.UtcNow);
        LogSent(logger);
        return Task.FromResult(new EmailSendResult(EmailSendStatus.Sent));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail fake descartado.")]
    private static partial void LogSent(ILogger logger);
}
