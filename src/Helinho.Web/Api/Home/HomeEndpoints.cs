using Helinho.Application.Contracts;

namespace Helinho.Web.Api.Home;

public static class HomeEndpoints
{
    public static RouteGroupBuilder MapHomeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/profile", () => ApiResults.NotImplemented())
            .Produces<ProfileResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/live-event", () => ApiResults.NotImplemented())
            .Produces<LiveEventResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/quizzes/progress", () => ApiResults.NotImplemented())
            .Produces<QuizProgressResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
