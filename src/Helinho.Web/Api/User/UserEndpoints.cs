using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.Profile;
using Helinho.Application.Quizzes;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Api.User;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/user/stats", async (IAppDbContext db, ICurrentStudent current, CancellationToken ct) =>
                await GetUserStats.HandleAsync(db, current, ct) is { } stats
                    ? Results.Ok(stats)
                    : Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden))
            .Produces<UserStatsResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        api.MapGet("/user/badges", ([FromQuery] int? page) => ApiResults.NotImplemented())
            .Produces<BadgesResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/user/inventory", async (IAppDbContext db, ICurrentStudent current, CancellationToken ct) =>
                await UserInventory.GetAsync(db, current, ct) is { } inventory ? Results.Ok(inventory) : Forbidden())
            .Produces<InventoryResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        api.MapPost("/user/inventory", async (
                InventoryRequest? request, IAppDbContext db, ICurrentStudent current, CancellationToken ct) =>
            {
                try
                {
                    return await UserInventory.ReplaceAsync(db, current, request, ct) is { } saved ? Results.Ok(saved) : Forbidden();
                }
                catch (InvalidInventoryException)
                {
                    return Results.Json(ApiErrors.CreateInvalidInventory(), statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .Produces<InventoryResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        return api;
    }

    private static IResult Forbidden() =>
        Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden);
}
