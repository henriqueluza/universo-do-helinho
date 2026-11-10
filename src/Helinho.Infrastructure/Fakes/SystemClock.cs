using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Fakes;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
