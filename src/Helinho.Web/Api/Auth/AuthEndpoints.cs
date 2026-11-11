using Helinho.Application.Contracts;

namespace Helinho.Web.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        // POST /login é a única rota sem Authorization.
        api.MapPost("/login", (LoginRequest request) => ApiResults.NotImplemented())
            .AllowAnonymous()
            .Produces<LoginResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);

        api.MapPost("/logout", () => ApiResults.NotImplemented())
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }
}
