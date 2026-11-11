using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PanelAdministradoresTests(PostgresFixture postgres)
{
    private const string SenhaInicial = "Segredo-Inicial9x";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private async Task<HelinhoWebFactory> NewFactoryAsync(CaptureLogger? logs = null)
    {
        var factory = new HelinhoWebFactory(postgres, configure: s =>
        {
            if (logs is not null) s.AddLogging(b => b.AddProvider(logs));
        });
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await (await client.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
        fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    private static async Task<Guid> CreateUnitAsync(HelinhoWebFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var unit = new Unit(name);
        db.Units.Add(unit);
        await db.SaveChangesAsync(Ct);
        return unit.Id;
    }

    private static Dictionary<string, string> AdminForm(Guid unitId, string user, string password = SenhaInicial) => new()
    {
        ["Input.UserName"] = user,
        ["Input.Email"] = $"{user}@exemplo.test",
        ["Input.UnitId"] = unitId.ToString(),
        ["Input.Password"] = password,
    };

    [Theory]
    [InlineData("/Administradores")]
    [InlineData("/Administradores/Novo")]
    public async Task Administrador_comum_recebe_403_por_url_direta(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var redirect = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var response = await client.GetAsync(redirect.Headers.Location, Ct);

        Assert.Equal("/acesso-negado", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Administrador_comum_nao_cria_por_post()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var response = await client.PostAsync("/Administradores/Novo", new FormUrlEncodedContent(new Dictionary<string, string>()), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/acesso-negado", new Uri(new Uri("http://localhost"), response.Headers.Location!).AbsolutePath);
    }

    [Fact]
    public async Task Master_cria_administrador_vinculado_a_unidade_sem_expor_ou_logar_a_senha()
    {
        var logs = new CaptureLogger();
        await using var factory = await NewFactoryAsync(logs);
        var unitId = await CreateUnitAsync(factory, "Unidade Aurora");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.aurora"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await (await client.GetAsync("/Administradores", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("adm.aurora", list);
        Assert.Contains("Unidade Aurora", list);
        Assert.DoesNotContain(SenhaInicial, list);
        lock (logs.Lines)
        {
            Assert.DoesNotContain(logs.Lines, l => l.Contains(SenhaInicial, StringComparison.Ordinal));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var created = await users.FindByNameAsync("adm.aurora");
        Assert.Equal(unitId, created!.UnitId);
        Assert.True(await users.IsInRoleAsync(created, RoleNames.Admin));
        Assert.True(await users.CheckPasswordAsync(created, SenhaInicial));
    }

    [Fact]
    public async Task Senha_fraca_devolve_erro_em_portugues_e_nao_repete_a_senha_na_tela()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Fraca");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.fraca", "abc"));

        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A senha deve ter pelo menos", html);
        Assert.DoesNotContain("value=\"abc\"", html);
    }

    [Fact]
    public async Task Senha_sem_simbolo_e_recusada_e_a_ajuda_da_tela_menciona_o_simbolo()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Sem Simbolo");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var form = await (await client.GetAsync("/Administradores/Novo", Ct)).Content.ReadAsStringAsync(Ct);
        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.simbolo", "Segredo123"));

        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
        Assert.Contains("número e um símbolo", System.Net.WebUtility.HtmlDecode(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A senha deve ter pelo menos um símbolo.", html);
    }

    [Fact]
    public async Task Unidade_tem_no_maximo_um_administrador_ativo()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Única");
        var outraUnidade = await CreateUnitAsync(factory, "Unidade Outra");
        using var client = factory.CreateFormClientAs(RoleNames.Master);
        Assert.Equal(HttpStatusCode.Redirect,
            (await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.um"))).StatusCode);

        var segundo = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.dois"));
        var emOutra = await PostFormAsync(client, "/Administradores/Novo", AdminForm(outraUnidade, "adm.tres"));

        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Contains("já tem um administrador ativo", WebUtility.HtmlDecode(await segundo.Content.ReadAsStringAsync(Ct)));
        Assert.Equal(HttpStatusCode.Redirect, emOutra.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByNameAsync("adm.dois"));
    }

    [Fact]
    public async Task Administrador_inativo_nao_bloqueia_novo_administrador_da_unidade()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Troca");
        using var client = factory.CreateFormClientAs(RoleNames.Master);
        await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.antigo"));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var antigo = (await users.FindByNameAsync("adm.antigo"))!;
            await users.SetLockoutEndDateAsync(antigo, DateTimeOffset.UtcNow.AddYears(10));
        }

        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.novo"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Criar_sem_antiforgery_e_recusado()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Sem Token");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await client.PostAsync("/Administradores/Novo",
            new FormUrlEncodedContent(AdminForm(unitId, "adm.sem.token")), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Campos_vazios_devolvem_mensagens_em_portugues_com_foco_no_resumo()
    {
        await using var factory = await NewFactoryAsync();
        await CreateUnitAsync(factory, "Unidade Vazia");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var response = await PostFormAsync(client, "/Administradores/Novo", new Dictionary<string, string>
        {
            ["Input.UserName"] = "",
            ["Input.Email"] = "",
            ["Input.UnitId"] = "",
            ["Input.Password"] = "",
        });

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o nome de usuário.", html);
        Assert.Contains("Informe o e-mail.", html);
        Assert.Contains("Selecione a unidade.", html);
        Assert.Contains("Informe a senha inicial.", html);
        Assert.Contains("id=\"resumo-erros\"", html);
        Assert.Matches("id=\"resumo-erros\"[^>]*autofocus", html);
        Assert.DoesNotContain("name=\"Input.UserName\" maxlength=\"60\" autocomplete=\"off\" autofocus", html);
    }
}
