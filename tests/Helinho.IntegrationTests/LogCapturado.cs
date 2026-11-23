using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

/// <summary>Guarda a mensagem, a exceção e os valores estruturados de tudo o que a aplicação registra, em qualquer nível.</summary>
public sealed class LogCapturado : ILoggerProvider, ILogger
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Linhas.Enqueue($"{logLevel}: {formatter(state, exception)}{exception}");
        foreach (var par in state as IEnumerable<KeyValuePair<string, object?>> ?? [])
        {
            Linhas.Enqueue($"{par.Key}={par.Value}");
        }
    }

    public void Dispose() => Linhas.Clear();
}
