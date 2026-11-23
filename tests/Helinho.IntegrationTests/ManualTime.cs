namespace Helinho.IntegrationTests;

/// <summary>Relógio de teste: a hora só anda com <see cref="Advance"/>, que também dispara os temporizadores vencidos.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start.ToUniversalTime();

    /// <summary>Sinaliza cada temporizador criado: o teste espera o laço chegar à espera antes de avançar a hora.</summary>
    public SemaphoreSlim TimerCreated { get; } = new(0);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.Ticks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(() => callback(state), _now + dueTime);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        TimerCreated.Release();
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        ManualTimer[] due;
        lock (_timers)
        {
            _now += by;
            due = [.. _timers.Where(t => t.Due <= _now)];
            _timers.RemoveAll(t => t.Due <= _now);
        }

        Array.ForEach(due, t => t.Fire?.Invoke());
    }

    private sealed class ManualTimer(Action fire, DateTimeOffset due) : ITimer
    {
        public Action? Fire { get; private set; } = fire;

        public DateTimeOffset Due => due;

        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() => Fire = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
