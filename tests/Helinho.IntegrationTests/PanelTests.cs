using System.Net;
using System.Security.Claims;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PanelTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // O Location do cookie middleware vem absoluto (http://localhost/...); compara só o caminho.
    private static string LocationPath(HttpResponseMessage response) =>
        new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Alunos")]
    [InlineData("/")]
    public async Task Pagina_sem_login_redireciona_para_entrar(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", LocationPath(response));
    }

    [Fact]
    public async Task Pagina_de_entrada_abre_sem_login()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/entrar", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Entrar_sem_antiforgery_e_recusado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = "x",
            ["Input.Password"] = "y",
        });

        var response = await client.PostAsync("/entrar", form, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Student_nao_entra_no_painel()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Student, Guid.NewGuid());

        var response = await client.GetAsync("/Dashboard", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/acesso-negado", LocationPath(response));
    }

    [Fact]
    public async Task Cookie_do_painel_e_HttpOnly_e_SameSite()
    {
        await using var factory = await NewFactoryAsync();

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.True(options.Cookie.HttpOnly);
        Assert.NotEqual(SameSiteMode.None, options.Cookie.SameSite);
        Assert.Equal("/entrar", options.LoginPath.Value);
    }

    [Theory]
    [InlineData(RoleNames.Master, "/Dashboard")]
    [InlineData(RoleNames.Master, "/Alunos")]
    [InlineData(RoleNames.Admin, "/Dashboard")]
    [InlineData(RoleNames.Admin, "/Alunos")]
    public async Task Master_e_Admin_abrem_as_paginas_do_painel(string role, string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(role, Guid.NewGuid());

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Layout_bare_carrega_o_tema()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var html = await (await client.GetAsync("/entrar", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("href=\"/css/theme.css", html);
    }

    [Fact]
    public async Task Layout_do_painel_carrega_o_tema()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var html = await (await client.GetAsync("/Dashboard", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("href=\"/css/theme.css", html);
    }

    [Fact]
    public async Task Tema_css_e_servido_como_text_css()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/css/theme.css", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void CurrentUser_le_papel_e_unidade_das_claims()
    {
        var unitId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, RoleNames.Admin),
                    new Claim(AppClaims.UnitId, unitId.ToString()),
                ],
                "Test")),
        };
        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.True(user.IsAuthenticated);
        Assert.Equal(userId, user.UserId);
        Assert.Equal(unitId, user.UnitId);
        Assert.True(user.IsInRole(RoleNames.Admin));
        Assert.False(user.IsInRole(RoleNames.Master));
    }

    [Fact]
    public void CurrentUser_sem_contexto_e_anonimo()
    {
        var user = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.False(user.IsAuthenticated);
        Assert.Null(user.UnitId);
        Assert.False(user.IsInRole(RoleNames.Master));
    }
}
