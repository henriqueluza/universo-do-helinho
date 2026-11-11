using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Application.Devices;

namespace Helinho.Web.Api.Devices;

public static class DevicesEndpoints
{
    public static RouteGroupBuilder MapDevicesEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/devices/push-token", async (
                PushTokenRequest? request, IAppDbContext db, ICurrentStudent current, IClock clock, CancellationToken ct) =>
            {
                try
                {
                    return await RegisterPushToken.HandleAsync(db, current, clock, request, ct)
                        ? Results.NoContent()
                        : Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden);
                }
                catch (InvalidPushTokenException)
                {
                    return Results.Json(ApiErrors.CreateInvalidPushToken(), statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden);
        return api;
    }
}
