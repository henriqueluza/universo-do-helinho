using Helinho.Application.Contracts;

namespace Helinho.Web.Api.Devices;

public static class DevicesEndpoints
{
    public static RouteGroupBuilder MapDevicesEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/devices/push-token", (PushTokenRequest request) => ApiResults.NotImplemented())
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
