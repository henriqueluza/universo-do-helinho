using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.Quests;

namespace Helinho.Web.Api.Quests;

public static class QuestsEndpoints
{
    public static RouteGroupBuilder MapQuestsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/quests/daily", async (IAppDbContext db, ICurrentStudent current, IClock clock, CancellationToken ct) =>
                await GetDailyQuests.HandleAsync(db, current, clock, ct) is { } daily
                    ? Results.Ok(daily)
                    : Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden))
            .Produces<DailyQuestsResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        api.MapGet("/quests/daily-bonus", () => Results.Ok(GetDailyQuests.Bonus()))
            .Produces<DailyBonusResponse>();
        return api;
    }
}
