using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Builder;
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
    private const string Forged = "198.51.100.7";
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

    /// <summary>
    /// O <c>migrate</c> do pre-deploy (ADR-0002 §7.3): cria o esquema antes de a página ser pedida. O token antiforgery
    /// de <c>/entrar</c> é protegido com as chaves da tabela <c>DataProtectionKeys</c> (§9); sem ela, a página não renderiza.
    /// </summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory) =>
        Assert.Equal(0, await SeedCommand.RunDatabaseCommandAsync(factory.Services, ["migrate"], TextWriter.Null, Ct));

    /// <summary>Requisição como o proxy a entrega: HTTP puro, com o esquema e o IP do cliente em cabeçalhos.</summary>
    private static HttpRequestMessage Proxied(HttpMethod method, string path, string? proto = "https", params string[] realIp)
    {
        var request = new HttpRequestMessage(method, path);
        if (proto is not null) request.Headers.Add("X-Forwarded-Proto", proto);
        if (realIp.Length > 0) request.Headers.Add("X-Real-IP", realIp);
        return request;
    }

    /// <summary>Login inválido na API. Mais de um valor em <paramref name="realIp"/> simula o cabeçalho duplicado: o do cliente e, por último, o da borda.</summary>
    private static async Task<HttpStatusCode> ApiLoginAsync(HttpClient client, string? forgedForwardedFor, params string[] realIp)
    {
        using var request = Proxied(HttpMethod.Post, "/api/login", realIp: realIp);
        request.Content = JsonContent.Create(new { login = "inexistente", password = "errada" });
        if (forgedForwardedFor is not null) request.Headers.Add("X-Forwarded-For", forgedForwardedFor);
        return (await client.SendAsync(request, Ct)).StatusCode;
    }

    private static Task<HttpStatusCode> ApiLoginAsync(HttpClient client, string realIp) => ApiLoginAsync(client, null, realIp);

    private static bool IsSecure(HttpResponseMessage response, string cookieName) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal))
            .Contains("; secure", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Fora_de_Development_http_redireciona_para_https_e_as_rotas_de_saude_nao()
    {
        // /health/ready nasce na Issue #96: até lá, uma rota de teste ocupa o lugar dela, depois do filtro da área.
        await using var factory = CreateFactory(Environments.Production, trustProxy: true)
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient<IStartupFilter, ProbeRoutes>()));
        using var client = CreateClient(factory);

        // X-Forwarded-Host é do cliente e não é processado: o destino do redirecionamento sai do Host real.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/entrar");
        request.Headers.Add("X-Forwarded-Host", "malicioso.exemplo.test");
        var panel = await client.SendAsync(request, Ct);
        var health = await client.GetAsync("/health", Ct);
        var ready = await client.GetAsync("/health/ready", Ct);
        var probe = await client.GetAsync("/sonda", Ct);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, panel.StatusCode);
        Assert.Equal($"https://{Host}/entrar", panel.Headers.Location!.OriginalString);
        Assert.False(panel.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        // A mesma rota de teste fora do prefixo é redirecionada: o 200 acima vem da exceção de /health, não da rota.
        Assert.Equal(HttpStatusCode.TemporaryRedirect, probe.StatusCode);
    }

    [Fact]
    public async Task Proto_encaminhado_e_tratado_como_https_com_HSTS_e_cookie_Secure()
    {
        await using var factory = CreateFactory(Environments.Production, trustProxy: true);
        await MigrateAsync(factory);
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
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var request = Proxied(HttpMethod.Get, "/entrar");
        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.False(IsSecure(response, "helinho.antiforgery"));
    }

    /// <summary>
    /// Em Development o cookie do painel segue o esquema da requisição (SameAsRequest), então ele e o desafio do
    /// cookie (emitido pelo middleware de autorização) só saem em https se o esquema já chegar corrigido até eles.
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

        using var anonymous = Proxied(HttpMethod.Get, "/", proto);
        var challenge = await client.SendAsync(anonymous, Ct);
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

        Assert.StartsWith($"{proto ?? "http"}://{Host}/entrar", challenge.Headers.Location!.OriginalString, StringComparison.Ordinal);
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
        // X-Forwarded-For é do cliente e não é lido: forjá-lo não troca de balde.
        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, forgedForwardedFor: Forged, ClientA));
        // X-Real-IP forjado pelo cliente: só o último valor (o escrito pela borda) conta, então também não troca de balde.
        Assert.Equal(HttpStatusCode.TooManyRequests, await ApiLoginAsync(client, null, Forged, ClientA));
        Assert.Equal(HttpStatusCode.Unauthorized, await ApiLoginAsync(client, ClientB));
    }

    [Fact]
    public async Task Com_a_confianca_desligada_X_Real_IP_forjado_nao_troca_de_balde_no_limite_de_taxa()
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

    private sealed class ProbeRoutes : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map("/health/ready", branch => branch.Run(_ => Task.CompletedTask));
            app.Map("/sonda", branch => branch.Run(_ => Task.CompletedTask));
            next(app);
        };
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
