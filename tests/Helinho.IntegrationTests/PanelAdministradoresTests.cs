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

    private static Dictionary<string, string> AdminForm(Guid unitId, string user, string? email = null) => new()
    {
        ["Input.UserName"] = user,
        ["Input.Email"] = email ?? $"{user}@exemplo.test",
        ["Input.UnitId"] = unitId.ToString(),
    };

    private static async Task<string> ListaAsync(HttpClient client) =>
        WebUtility.HtmlDecode(await (await client.GetAsync("/Administradores", Ct)).Content.ReadAsStringAsync(Ct));

    private static async Task<string> SituacaoAsync(HttpClient client, string user) =>
        Regex.Match(await ListaAsync(client), $"<th scope=\"row\">{Regex.Escape(user)}</th>.*?<span class=\"tag [^\"]+\">([^<]+)</span>",
            RegexOptions.Singleline).Groups[1].Value;

    private static async Task<int> SqlAsync(HelinhoWebFactory factory, FormattableString sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlAsync(sql, Ct);
    }

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
    public async Task Master_cria_administrador_sem_senha_e_o_convite_fica_na_fila_so_com_referencias()
    {
        var logs = new CaptureLogger();
        await using var factory = await NewFactoryAsync(logs);
        var unitId = await CreateUnitAsync(factory, "Unidade Aurora");
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        var form = await (await client.GetAsync("/Administradores/Novo", Ct)).Content.ReadAsStringAsync(Ct);
        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.aurora"));

        Assert.Contains("link para criar a própria senha. O link vale 72 horas.", WebUtility.HtmlDecode(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await ListaAsync(client);
        Assert.Contains("adm.aurora", list);
        Assert.Contains("Unidade Aurora", list);
        Assert.Equal("Aguardando envio", await SituacaoAsync(client, "adm.aurora"));
        lock (logs.Lines)
        {
            Assert.DoesNotContain(logs.Lines, l => l.Contains("adm.aurora@exemplo.test", StringComparison.OrdinalIgnoreCase));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var created = await users.FindByNameAsync("adm.aurora");
        Assert.Equal(unitId, created!.UnitId);
        Assert.True(await users.IsInRoleAsync(created, RoleNames.Admin));
        Assert.False(await users.HasPasswordAsync(created));
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().QueueJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("email.admin-invite", created.Id, QueueJobStatus.Pending), (job.Type, job.SubjectId, job.Status));
        Assert.Equal("""{"v":1}""", job.Payload.Replace(" ", "", StringComparison.Ordinal));
        Assert.StartsWith($"email.admin-invite:{created.Id:D}:", job.IdempotencyKey);
    }

    [Fact]
    public async Task Email_de_outra_conta_do_painel_e_recusado_sem_criar_nem_enfileirar()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Eco");
        var outraUnidade = await CreateUnitAsync(factory, "Unidade Eco Dois");
        using var client = factory.CreateFormClientAs(RoleNames.Master);
        await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.eco"));

        var response = await PostFormAsync(client, "/Administradores/Novo", AdminForm(outraUnidade, "adm.eco.dois", "ADM.ECO@exemplo.test"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Já existe um usuário com este e-mail.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync("adm.eco.dois"));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().QueueJobs.CountAsync(Ct));
    }

    [Fact]
    public async Task Lista_mostra_a_situacao_do_convite_lida_da_fila()
    {
        await using var factory = await NewFactoryAsync();
        var unitId = await CreateUnitAsync(factory, "Unidade Situação");
        using var client = factory.CreateFormClientAs(RoleNames.Master);
        await PostFormAsync(client, "/Administradores/Novo", AdminForm(unitId, "adm.situacao"));

        await SqlAsync(factory, $"""UPDATE "QueueJobs" SET "Status" = 'Succeeded', "FinishedAt" = now() - interval '71 hours'""");
        Assert.Equal("Convite enviado", await SituacaoAsync(client, "adm.situacao"));
        await SqlAsync(factory, $"""UPDATE "QueueJobs" SET "FinishedAt" = now() - interval '73 hours'""");
        Assert.Equal("Convite expirado", await SituacaoAsync(client, "adm.situacao"));
        await SqlAsync(factory, $"""UPDATE "QueueJobs" SET "Status" = 'Failed'""");
        Assert.Equal("Não entregue", await SituacaoAsync(client, "adm.situacao"));
        await SqlAsync(factory, $"""DELETE FROM "QueueJobs" """);
        Assert.Equal("Convite expirado", await SituacaoAsync(client, "adm.situacao"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.AddPasswordAsync((await users.FindByNameAsync("adm.situacao"))!, "Senha-Sintetica-9x")).Succeeded);
        }

        Assert.Equal("Ativo", await SituacaoAsync(client, "adm.situacao"));
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
        });

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o nome de usuário.", html);
        Assert.Contains("Informe o e-mail.", html);
        Assert.Contains("Selecione a unidade.", html);
        Assert.Contains("id=\"resumo-erros\"", html);
        Assert.Matches("id=\"resumo-erros\"[^>]*autofocus", html);
        Assert.DoesNotContain("name=\"Input.UserName\" maxlength=\"60\" autocomplete=\"off\" autofocus", html);
    }
}
