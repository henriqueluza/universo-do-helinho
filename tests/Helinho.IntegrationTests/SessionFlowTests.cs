using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Helinho.IntegrationTests;

/// <summary>Ciclo de sessão de ponta a ponta num tablet compartilhado: login, rota autenticada, logout, troca de aluno.</summary>
[Collection(PostgresCollection.Name)]
public class SessionFlowTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private async Task<(WebApplicationFactory<Program> Factory, FakeClock Clock)> CreateFactoryAsync()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var factory = new HelinhoWebFactory(postgres).WithWebHostBuilder(b =>
        {
            b.UseSetting("Api:Auth:LoginRateLimit", "1000");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(Ct);
        return (factory, clock);
    }

    private static async Task<string> CreateStudentAsync(IServiceProvider services, bool approved = true)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(RoleNames.Student))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(RoleNames.Student));
        }

        var tag = Guid.NewGuid().ToString("N")[..10];
        var user = new AppUser { UserName = $"u{tag}", Email = $"u{tag}@exemplo.test" };
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        await users.AddToRoleAsync(user, RoleNames.Student);

        var db = sp.GetRequiredService<AppDbContext>();
        var unit = new Unit($"Unidade {tag}");
        db.Units.Add(unit);
        var student = new Student(
            user.Id, unit.Id, $"M{tag}", "Aluno Sintético", $"rg{tag}", $"cpf{tag}", "Rua Fictícia, 1",
            new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 10));
        if (approved)
        {
            student.ApproveByGuardian();
        }

        db.Students.Add(student);
        await db.SaveChangesAsync(Ct);
        return $"M{tag}";
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password = Password) =>
        client.PostAsJsonAsync("/api/login", new { login, password }, Ct);

    private static async Task<string> TokenAsync(HttpClient client, string login) =>
        (await (await LoginAsync(client, login)).Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;

    private static HttpRequestMessage Get(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(["error", "message"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task Aluno_A_sai_e_o_token_dele_vira_token_expired_sem_afetar_o_aluno_B_no_mesmo_cliente()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var a = await CreateStudentAsync(factory.Services);
        var b = await CreateStudentAsync(factory.Services);
        using var tablet = factory.CreateClient(); // um único cliente, como o tablet compartilhado

        var tokenA = await TokenAsync(tablet, a);
        var selection = new { selected = new[] { new { id = "cabelo-a", name = "Cabelo de A", type = "cabelo" } } };
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/user/inventory") { Content = JsonContent.Create(selection) };
        save.Headers.Authorization = new("Bearer", tokenA);
        Assert.Equal(HttpStatusCode.OK, (await tablet.SendAsync(save, Ct)).StatusCode);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        logout.Headers.Authorization = new("Bearer", tokenA);
        Assert.Equal(HttpStatusCode.NoContent, (await tablet.SendAsync(logout, Ct)).StatusCode);

        Assert.Equal("token_expired", await ErrorCodeAsync(await tablet.SendAsync(Get("/api/user/inventory", tokenA), Ct), HttpStatusCode.Unauthorized));

        var tokenB = await TokenAsync(tablet, b);
        var inventoryB = await tablet.SendAsync(Get("/api/user/inventory", tokenB), Ct);
        Assert.Equal(HttpStatusCode.OK, inventoryB.StatusCode);
        var body = await inventoryB.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("cabelo-a", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Cabelo de A", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Os_tres_erros_se_distinguem_pelo_corpo_e_passam_pelo_verificador_de_contrato()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var approved = await CreateStudentAsync(factory.Services);
        var pending = await CreateStudentAsync(factory.Services, approved: false);
        using var client = factory.CreateClient();

        var invalid = await ErrorCodeAsync(await LoginAsync(client, approved, "senha-errada"), HttpStatusCode.Unauthorized);
        var forbidden = await ErrorCodeAsync(await LoginAsync(client, pending), HttpStatusCode.Forbidden);
        var expired = await ErrorCodeAsync(await client.SendAsync(Get("/api/user/stats", "desconhecido"), Ct), HttpStatusCode.Unauthorized);

        Assert.Equal("invalid_credentials", invalid);
        Assert.Equal("forbidden", forbidden);
        Assert.Equal("token_expired", expired);
    }

    [Fact]
    public async Task Token_vencido_pelo_relogio_de_teste_responde_token_expired()
    {
        var (factory, clock) = await CreateFactoryAsync();
        await using var _f = factory;
        var account = await CreateStudentAsync(factory.Services);
        using var client = factory.CreateClient();
        var token = await TokenAsync(client, account);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/api/user/stats", token), Ct)).StatusCode);

        clock.UtcNow = clock.UtcNow.AddHours(8).AddSeconds(1);

        Assert.Equal("token_expired", await ErrorCodeAsync(await client.SendAsync(Get("/api/user/stats", token), Ct), HttpStatusCode.Unauthorized));
    }
}
