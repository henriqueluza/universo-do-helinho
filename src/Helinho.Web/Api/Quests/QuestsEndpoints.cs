using Helinho.Application.Contracts;

namespace Helinho.Web.Api.Quests;

public static class QuestsEndpoints
{
    public static RouteGroupBuilder MapQuestsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/quests/daily", () => ApiResults.NotImplemented())
            .Produces<DailyQuestsResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/quests/daily-bonus", () => ApiResults.NotImplemented())
            .Produces<DailyBonusResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
