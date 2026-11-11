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

    /// <summary>Id fixo do usuário; necessário quando o teste envia formulário (o token antiforgery é ligado ao usuário).</summary>
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var name = Request.Headers[NameHeader].FirstOrDefault() ?? "Pessoa de Teste";
        // Id estável por (papel, nome, unidade): o token antiforgery do GET precisa valer no POST seguinte.
        var seed = $"{role}|{name}|{Request.Headers[UnitHeader]}";
        var id = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))[..16]);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Request.Headers[UserHeader].FirstOrDefault() ?? id.ToString()),
            new(ClaimTypes.Name, name),
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

    /// <summary>Como <see cref="CreateClientAs"/>, com id de usuário estável entre requisições (para POST com antiforgery).</summary>
    public static HttpClient CreateFormClientAs(this HelinhoWebFactory factory, string role, Guid? unitId = null)
    {
        var client = factory.CreateClientAs(role, unitId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        return client;
    }

    /// <summary>Cliente anônimo, sem redirecionamento automático.</summary>
    public static HttpClient CreateAnonymousClient(this HelinhoWebFactory factory) =>
        factory.CreateClient(new() { AllowAutoRedirect = false });
}
