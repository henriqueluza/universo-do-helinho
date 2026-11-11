using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.LiveEvents;

/// <summary>Caso de uso do GET /live-event: evento ativo que termina primeiro, ou `has_event: false` (P5).</summary>
public static class GetLiveEvent
{
    private static readonly DateTime EmptyDate = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    public static async Task<LiveEventResponse> HandleAsync(
        IAppDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var active = await db.LiveEvents
            .Where(e => e.EndsAt > now)
            .OrderBy(e => e.EndsAt)
            .ThenBy(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return active is null
            ? new LiveEventResponse(false, "", "", "", "", EmptyDate)
            : new LiveEventResponse(
                true, active.Id.ToString(), active.Title, active.Subtitle,
                active.QuizType.ToString().ToLowerInvariant(), active.EndsAt.UtcDateTime);
    }
}
