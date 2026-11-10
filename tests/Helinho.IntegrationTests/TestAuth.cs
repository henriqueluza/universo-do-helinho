using System.Security.Claims;
using System.Text.Encodings.Web;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>
/// Autenticação de teste para as frentes do painel: o cliente declara papel, unidade e nome por cabeçalhos
/// e o handler monta as mesmas claims do login real. Sem cabeçalho, a requisição segue anônima.
/// Uso: <c>factory.CreateClientAs(RoleNames.Admin, unitId)</c>.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";
    public const string UnitHeader = "X-Test-Unit";
    public const string NameHeader = "X-Test-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, Request.Headers[NameHeader].FirstOrDefault() ?? "Pessoa de Teste"),
            new(ClaimTypes.Role, role.ToString()),
        };
        if (Request.Headers.TryGetValue(UnitHeader, out var unit))
        {
            claims.Add(new Claim(AppClaims.UnitId, unit.ToString()));
        }

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class TestAuthExtensions
{
    /// <summary>Cliente já autenticado com o papel, a unidade e o nome informados (sem redirecionamento automático).</summary>
    public static HttpClient CreateClientAs(
        this HelinhoWebFactory factory, string role, Guid? unitId = null, string name = "Pessoa de Teste")
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, name);
        if (unitId is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UnitHeader, id.ToString());
        }

        return client;
    }

    /// <summary>Cliente anônimo, sem redirecionamento automático.</summary>
    public static HttpClient CreateAnonymousClient(this HelinhoWebFactory factory) =>
        factory.CreateClient(new() { AllowAutoRedirect = false });
}
