using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Api;

public static class ApiAuth
{
    public const string Scheme = "ApiBearer";
    public const string Policy = "ApiStudent";
}

/// <summary>Esquema Bearer da API do jogo. Delega a validação a <see cref="IApiSessionValidator"/>.</summary>
public sealed class ApiBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiSessionValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Prefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await validator.ValidateAsync(header[Prefix.Length..].Trim(), Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("token_expired");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.StudentId.ToString())], ApiAuth.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiAuth.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteErrorAsync(StatusCodes.Status401Unauthorized, ApiErrors.CreateTokenExpired());

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteErrorAsync(StatusCodes.Status403Forbidden, ApiErrors.CreateForbidden());

    private Task WriteErrorAsync(int status, ErrorResponse body)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(body, ApiJson.CreateOptions(), Context.RequestAborted);
    }
}

/// <summary>Aluno da requisição atual, lido do principal montado pelo <see cref="ApiBearerHandler"/>.</summary>
public sealed class HttpContextCurrentStudent(IHttpContextAccessor accessor) : ICurrentStudent
{
    public Guid StudentId
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new InvalidOperationException("Requisição sem aluno autenticado.");
        }
    }
}
