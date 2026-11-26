namespace Helinho.IntegrationTests;

/// <summary>Relógio de teste: a hora só anda com <see cref="Advance"/>.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start.ToUniversalTime();

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.Ticks;

    public void Advance(TimeSpan by) => _now += by;
}
