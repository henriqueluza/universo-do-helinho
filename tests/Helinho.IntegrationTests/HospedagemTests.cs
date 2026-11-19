using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Helinho.IntegrationTests;

/// <summary>Issue #97: a aplicação atrás do proxy que termina o TLS (cabeçalhos do proxy, HTTPS, HSTS, cookies Secure).</summary>
[Collection(PostgresCollection.Name)]
public class HospedagemTests(PostgresFixture postgres)
{
    // O HSTS do ASP.NET Core ignora localhost; o painel é pedido por um host sintético.
    private const string Host = "painel.exemplo.test";
    private const string Password = "Senha-Sintetica-1";
    private const string ClientA = "203.0.113.10";
    private const string ClientB = "203.0.113.20";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(string environment, bool trustProxy, int rateLimit = 1000) =>
        new HelinhoWebFactory(postgres).WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Proxy:TrustForwardedHeaders", trustProxy ? "true" : "false");
            b.UseSetting("Api:Auth:LoginRateLimit", rateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri($"http://{Host}") });

    private static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(Ct);
    }

    /// <summary>Requisição como o proxy a entrega: HTTP puro, com o esquema e o IP do cliente em cabeçalhos.</summary>
    private static HttpRequestMessage Proxied(HttpMethod method, string path, string? proto = "https", string? clientIp = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (proto is not null) request.Headers.Add("X-Forwarded-Proto", proto);
        if (clientIp is not null) request.Headers.Add("X-Real-IP", clientIp);
        return request;
    }

    private static async Task<HttpStatusCode> ApiLoginAsync(HttpClient client, string clientIp, string? forgedForwardedFor = null)
    {
        using var request = Proxied(HttpMethod.Post, "/api/login", clientIp: clientIp);
        request.Content = JsonContent.Create(new { login = "inexistente", password = "errada" });
        if (forgedForwardedFor is not null) request.Headers.Add("X-Forwarded-For", forgedForwardedFor);
        return (await client.SendAsync(request, Ct)).StatusCode;
    }

    private static bool IsSecure(HttpResponseMessage response, string cookieName) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal))
            .Contains("; secure", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Fora_de_Development_http_redireciona_para_https_e_as_rotas_de_saude_nao()
    {
        await using var factory = CreateFactory(Environments.Production, trustProxy: true);
        using var client = CreateClient(factory);

        var panel = await client.GetAsync("/entrar", Ct);
        var health = await client.GetAsync("/health", Ct);
        var ready = await client.GetAsync("/health/ready", Ct);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, panel.StatusCode);
        Assert.Equal($"https://{Host}/entrar", panel.Headers.Location!.OriginalString);
        Assert.False(panel.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        // /health/ready nasce na Issue #96; aqui só importa que o prefixo não é redirecionado.
        Assert.False((int)ready.StatusCode is >= 300 and < 400);
    }

    [Fact]
    public async Task Proto_encaminhado_e_tratado_como_https_com_HSTS_e_cookie_Secure()
    {
        await using var factory = CreateFactory(Environments.Production, trustProxy: true);
        using var client = CreateClient(factory);

        using var request = Proxied(HttpMethod.Get, "/entrar");
        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        Assert.True(IsSecure(response, "helinho.antiforgery"));
    }

    [Fact]
    public async Task Com_a_confianca_desligada_o_proto_encaminhado_e_ignorado()
    {
        await using var factory = CreateFactory(Environments.Production, trustProxy: false);
        using var client = CreateClient(factory);

        using var request = Proxied(HttpMethod.Get, "/entrar");
        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Em_Development_nao_ha_redirecionamento_nem_HSTS_nem_cookie_Secure()
    {
        await using var factory = CreateFactory(Environments.Development, trustProxy: false);
        using var client = CreateClient(factory);

        using var request = Proxied(HttpMethod.Get, "/entrar");
        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.False(IsSecure(response, "helinho.antiforgery"));
    }

    /// <summary>
    /// Em Development o cookie do painel segue o esquema da requisição (SameAsRequest): ele só sai Secure se o
    /// esquema já estiver corrigido quando a autenticação roda, o que prova a ordem do pipeline.
    /// </summary>
    [Theory]
    [InlineData("https", true)]
    [InlineData(null, false)]
    public async Task Cookie_de_login_sai_Secure_quando_o_proxy_informa_https(string? proto, bool secure)
    {
        await using var factory = CreateFactory(Environments.Development, trustProxy: true);
        await SeedAsync(factory);
        var user = await CreateAdminAsync(factory);
        using var client = CreateClient(factory);

        using var get = Proxied(HttpMethod.Get, "/entrar", proto);
        var html = await (await client.SendAsync(get, Ct)).Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var post = Proxied(HttpMethod.Post, "/entrar", proto);
        post.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.UserName"] = user,
            ["Input.Password"] = Password,
        });
        var response = await client.SendAsync(post, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(secure, IsSecure(response, "helinho.panel"));
    }

    [Fact]
    public async Task Limite_de_taxa_do_login_particiona_pelo_IP_do_cliente_informado_pelo_proxy()
    {
        await using var factory = CreateFactory(Environments.Production, trustProxy: true, rateLimit: 3);
        await SeedAsync(factory);
        using var client = CreateClient(factory);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await ApiLoginAsync(client, ClientA));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, ClientA));
        // X-Forwarded-For é do cliente: forjá-lo não troca de balde.
        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, ClientA, forgedForwardedFor: "198.51.100.7"));
        // Só o último valor (o escrito pela borda) conta: um valor injetado antes dele também não troca de balde.
        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, $"198.51.100.7, {ClientA}"));
        Assert.Equal(HttpStatusCode.Unauthorized, await ApiLoginAsync(client, ClientB));
    }

    [Fact]
    public async Task Com_a_confianca_desligada_o_IP_encaminhado_nao_particiona_o_limite_de_taxa()
    {
        await using var factory = CreateFactory(Environments.Development, trustProxy: false, rateLimit: 3);
        await SeedAsync(factory);
        using var client = CreateClient(factory);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await ApiLoginAsync(client, ClientA));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, ClientB));
    }

    private static async Task<string> CreateAdminAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(RoleNames.Admin))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(RoleNames.Admin));
        }

        var name = $"t{Guid.NewGuid():N}"[..12];
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@exemplo.test" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.Admin)).Succeeded);
        return name;
    }
}
