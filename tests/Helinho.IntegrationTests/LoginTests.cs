using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class LoginTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<string> CreateUserAsync(HelinhoWebFactory factory, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(role));
        }

        var name = $"t{Guid.NewGuid():N}"[..12];
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@exemplo.test" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return name;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string? userName = null, string? password = null, string? returnUrl = null)
    {
        var page = await client.GetAsync(path, Ct);
        var html = await page.Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        if (userName is not null) fields["Input.UserName"] = userName;
        if (password is not null) fields["Input.Password"] = password;
        if (returnUrl is not null) fields["ReturnUrl"] = returnUrl;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    [Theory]
    [InlineData(RoleNames.Master)]
    [InlineData(RoleNames.Admin)]
    public async Task Login_valido_de_painel_redireciona_e_abre_o_dashboard(string role)
    {
        await using var factory = await NewFactoryAsync();
        var user = await CreateUserAsync(factory, role);
        using var client = factory.CreateAnonymousClient();

        var response = await PostAsync(client, "/entrar", user, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("helinho.panel=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Senha_errada_e_Student_recebem_a_mesma_mensagem_sem_cookie()
    {
        await using var factory = await NewFactoryAsync();
        var admin = await CreateUserAsync(factory, RoleNames.Admin);
        var student = await CreateUserAsync(factory, RoleNames.Student);
        using var client = factory.CreateAnonymousClient();

        var wrong = await PostAsync(client, "/entrar", admin, "errada");
        var stud = await PostAsync(client, "/entrar", student, Password);

        foreach (var response in new[] { wrong, stud })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Usuário ou senha inválidos.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)));
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    [Fact]
    public async Task ReturnUrl_local_e_aceito_e_externo_e_ignorado()
    {
        await using var factory = await NewFactoryAsync();
        var user = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();

        var local = await PostAsync(client, "/entrar", user, Password, "/Alunos");
        var external = await PostAsync(client, "/entrar", user, Password, "https://mau.example/x");

        Assert.Equal("/Alunos", local.Headers.Location!.OriginalString);
        Assert.Equal("/", external.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_ate_a_senha_certa()
    {
        await using var factory = await NewFactoryAsync();
        var user = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();

        for (var i = 0; i < 5; i++)
        {
            await PostAsync(client, "/entrar", user, "errada");
        }

        var response = await PostAsync(client, "/entrar", user, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("bloqueado", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Erro_de_campo_marca_aria_invalid_e_liga_a_mensagem()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await PostAsync(client, "/entrar", "", "");
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("aria-describedby=\"Input_UserName-erro\"", html);
        Assert.Contains("id=\"Input_Password-erro\"", html);
        Assert.Contains("href=\"#Input_UserName\"", html);
    }

    [Fact]
    public async Task Sair_por_POST_encerra_a_sessao_e_por_GET_preserva()
    {
        await using var factory = await NewFactoryAsync();
        var user = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();
        await PostAsync(client, "/entrar", user, Password);

        var get = await client.GetAsync("/sair", Ct);
        // O esquema de teste é o autenticador padrão da fábrica; por isso a prova é o Set-Cookie, não uma página protegida.

        // /sair exige antiforgery: o token vem de uma página com formulário renderizada com a sessão ativa.
        var html = await (await client.GetAsync("/entrar", Ct)).Content.ReadAsStringAsync(Ct);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var logout = await client.PostAsync("/sair", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), Ct);

        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.False(get.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/entrar", logout.Headers.Location!.OriginalString);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.Contains("expires=Thu, 01 Jan 1970"));
    }
}
