using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.LiveEvents;
using Helinho.Application.Profile;
using Helinho.Application.Quizzes;

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
        api.MapGet("/live-event", async (IAppDbContext db, IClock clock, CancellationToken ct) =>
                Results.Ok(await GetLiveEvent.HandleAsync(db, clock, ct)))
            .Produces<LiveEventResponse>();
        api.MapGet("/quizzes/progress", async (IAppDbContext db, ICurrentStudent current, CancellationToken ct) =>
                await GetQuizProgress.HandleAsync(db, current, ct) is { } progress
                    ? Results.Ok(progress)
                    : Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden))
            .Produces<QuizProgressResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        return api;
    }
}
