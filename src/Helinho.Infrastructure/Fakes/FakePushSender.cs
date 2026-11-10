using Helinho.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.Fakes;

public sealed partial class FakePushSender(ILogger<FakePushSender> logger) : IPushSender
{
    public Task SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        LogSent(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push fake descartado.")]
    private static partial void LogSent(ILogger logger);
}
