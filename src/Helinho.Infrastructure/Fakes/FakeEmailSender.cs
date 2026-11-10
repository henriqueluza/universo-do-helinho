using Helinho.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.Fakes;

/// <summary>Não envia nada; registra só o fato do envio (sem destinatário nem corpo, por LGPD).</summary>
public sealed partial class FakeEmailSender(ILogger<FakeEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        LogSent(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail fake descartado.")]
    private static partial void LogSent(ILogger logger);
}
