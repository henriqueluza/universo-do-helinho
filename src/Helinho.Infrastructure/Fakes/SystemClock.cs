using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Fakes;

/// <summary>
/// Relógio do sistema. Lê a hora do <see cref="TimeProvider"/> registrado (em produção, <see cref="TimeProvider.System"/>),
/// o mesmo do processador da fila: quem enfileira e quem captura nunca ficam em relógios diferentes (ADR-0003 §6.1).
/// </summary>
public sealed class SystemClock(TimeProvider? time = null) : IClock
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public DateTimeOffset UtcNow => _time.GetUtcNow();
}
