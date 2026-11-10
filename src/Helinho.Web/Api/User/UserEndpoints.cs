using Helinho.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Api.User;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/user/stats", () => ApiResults.NotImplemented())
            .Produces<UserStatsResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/user/badges", ([FromQuery] int? page) => ApiResults.NotImplemented())
            .Produces<BadgesResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapGet("/user/inventory", () => ApiResults.NotImplemented())
            .Produces<InventoryResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        api.MapPost("/user/inventory", (InventoryRequest request) => ApiResults.NotImplemented())
            .Produces<InventoryResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
