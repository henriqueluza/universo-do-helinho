using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

/// <summary>Login do painel de ponta a ponta (cookie real, sem o esquema de teste) e recuperação de senha.</summary>
[Collection(PostgresCollection.Name)]
public class PanelSessionTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingEmail : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(formatter(state, exception) + exception);
            }
        }

        public void Dispose()
        {
        }
    }

    private async Task<HelinhoWebFactory> NewFactoryAsync(RecordingEmail? email = null, CaptureLogger? logs = null)
    {
        var factory = new HelinhoWebFactory(postgres, realCookieAuth: true, configure: s =>
        {
            if (email is not null) s.AddSingleton<IEmailSender>(email);
            if (logs is not null) s.AddLogging(b => b.AddProvider(logs));
        });
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<(string Name, string Email)> CreateUserAsync(HelinhoWebFactory factory, string role, Guid? unitId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(role));
        }

        var name = $"u{Guid.NewGuid():N}"[..12];
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = name, Email = $"{name}@exemplo.test", UnitId = unitId };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (name, user.Email!);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields, string tokenFrom)
    {
        var html = await (await client.GetAsync(tokenFrom, Ct)).Content.ReadAsStringAsync(Ct);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string user, string password) =>
        PostFormAsync(client, "/entrar", new() { ["Input.UserName"] = user, ["Input.Password"] = password }, "/entrar");

    [Fact]
    public async Task Master_entra_ve_nome_papel_e_unidade_e_sai()
    {
        await using var factory = await NewFactoryAsync();
        var (name, _) = await CreateUserAsync(factory, RoleNames.Master);
        using var client = factory.CreateAnonymousClient();

        var login = await LoginAsync(client, name, Password);
        var home = await client.GetStringAsync("/Dashboard", Ct);
        var logout = await PostFormAsync(client, "/sair", [], "/Dashboard");
        var after = await client.GetAsync("/Dashboard", Ct);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains(name, home);
        Assert.Contains(">Master<", home);
        Assert.Contains("Todas as unidades", home);
        Assert.Equal("/entrar", logout.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains("/entrar", after.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_entra_ve_nome_papel_e_unidade_e_sai()
    {
        await using var factory = await NewFactoryAsync();
        Guid unitId;
        string unitName;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unit = scope.ServiceProvider.GetRequiredService<AppDbContext>().Units.OrderBy(u => u.Name).First();
            (unitId, unitName) = (unit.Id, unit.Name);
        }

        var (name, _) = await CreateUserAsync(factory, RoleNames.Admin, unitId);
        using var client = factory.CreateAnonymousClient();

        await LoginAsync(client, name, Password);
        var home = await client.GetStringAsync("/Dashboard", Ct);
        await PostFormAsync(client, "/sair", [], "/Dashboard");
        var after = await client.GetAsync("/Dashboard", Ct);

        Assert.Contains(name, home);
        Assert.Contains("Administrador de unidade", home);
        Assert.Contains(unitName, WebUtility.HtmlDecode(Regex.Match(home, "user-block.*?</form>", RegexOptions.Singleline).Value));
        Assert.DoesNotContain("href=\"/Unidades\"", home);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Fact]
    public async Task Student_nao_entra_no_painel()
    {
        await using var factory = await NewFactoryAsync();
        var (name, _) = await CreateUserAsync(factory, RoleNames.Student);
        using var client = factory.CreateAnonymousClient();

        var login = await LoginAsync(client, name, Password);
        var home = await client.GetAsync("/Dashboard", Ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
    }

    [Fact]
    public async Task Usuario_inexistente_e_senha_errada_dao_a_mesma_resposta()
    {
        await using var factory = await NewFactoryAsync();
        var (name, _) = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();

        var ghost = await LoginAsync(client, "nao-existe", Password);
        var wrong = await LoginAsync(client, name, "errada");

        Assert.Equal(ghost.StatusCode, wrong.StatusCode);
        Assert.Contains("Usuário ou senha inválidos.", WebUtility.HtmlDecode(await ghost.Content.ReadAsStringAsync(Ct)));
        Assert.Contains("Usuário ou senha inválidos.", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Recuperar_senha_responde_igual_para_conta_existente_e_inexistente_e_so_usa_o_sender_fake()
    {
        var email = new RecordingEmail();
        await using var factory = await NewFactoryAsync(email);
        var (_, address) = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();

        async Task<string> RequestAsync(string to) => WebUtility.HtmlDecode(await (await PostFormAsync(
            client, "/recuperar-senha", new() { ["Input.Email"] = to }, "/recuperar-senha")).Content.ReadAsStringAsync(Ct));

        var known = await RequestAsync(address);
        var unknown = await RequestAsync("ninguem@exemplo.test");

        const string Confirmation = "Se o e-mail estiver cadastrado";
        Assert.Contains(Confirmation, known);
        Assert.Contains(Confirmation, unknown);
        var sent = Assert.Single(email.Sent);
        Assert.Equal(address, sent.To);
        Assert.DoesNotContain("token", sent.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recuperar_senha_com_e_mail_invalido_mostra_erro_de_campo()
    {
        await using var factory = await NewFactoryAsync(new RecordingEmail());
        using var client = factory.CreateAnonymousClient();

        var response = await PostFormAsync(client, "/recuperar-senha", new() { ["Input.Email"] = "x" }, "/recuperar-senha");

        Assert.Contains("aria-invalid=\"true\"", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Login_e_recuperacao_nao_gravam_senha_nem_token_em_log()
    {
        var logs = new CaptureLogger();
        await using var factory = await NewFactoryAsync(logs: logs);
        var (name, address) = await CreateUserAsync(factory, RoleNames.Admin);
        using var client = factory.CreateAnonymousClient();

        await LoginAsync(client, name, "Senha-Errada-Sintetica-9");
        await LoginAsync(client, name, Password);
        await PostFormAsync(client, "/recuperar-senha", new() { ["Input.Email"] = address }, "/recuperar-senha");

        string[] snapshot;
        lock (logs.Lines) snapshot = [.. logs.Lines];
        Assert.NotEmpty(snapshot);
        Assert.DoesNotContain(snapshot, l => l.Contains(Password, StringComparison.Ordinal)
            || l.Contains("Senha-Errada-Sintetica-9", StringComparison.Ordinal)
            || l.Contains(address, StringComparison.OrdinalIgnoreCase));
    }
}
