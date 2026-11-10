using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.Profile;

namespace Helinho.Web.Api.Home;

public static class HomeEndpoints
{
    public static RouteGroupBuilder MapHomeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/profile", async (IAppDbContext db, ICurrentStudent current, CancellationToken ct) =>
                await GetProfile.HandleAsync(db, current, ct) is { } profile
                    ? Results.Ok(profile)
                    : Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden))
            .Produces<ProfileResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        api.MapGet("/live-event", () => ApiResults.NotImplemented())
            .Produces<LiveEventResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/quizzes/progress", () => ApiResults.NotImplemented())
            .Produces<QuizProgressResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
