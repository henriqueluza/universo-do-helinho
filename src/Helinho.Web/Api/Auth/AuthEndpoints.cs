using Helinho.Application.Contracts;
using Helinho.Application.Identidade;
using Helinho.Web.Composition;

namespace Helinho.Web.Api.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        // POST /login é a única rota sem Authorization.
        api.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentidadeArea.LoginRateLimitPolicy)
            .Produces<LoginResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);

        api.MapPost("/logout", () => ApiResults.NotImplemented())
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status501NotImplemented);
        return api;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request, LoginHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request ?? new LoginRequest("", ""), cancellationToken);
        return result.Status switch
        {
            LoginStatus.Success => Results.Ok(result.Response),
            LoginStatus.Forbidden => Results.Json(ApiErrors.CreateForbidden(), statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(ApiErrors.CreateInvalidCredentials(), statusCode: StatusCodes.Status401Unauthorized),
        };
    }
}
