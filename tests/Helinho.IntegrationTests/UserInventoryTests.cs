using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>GET e POST /api/user/inventory (P10): seleção por aluno, um item por type, validação com 400.</summary>
[Collection(PostgresCollection.Name)]
public class UserInventoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Item(string id, string name, string type) => new { id, name, type };

    [Fact]
    public async Task Get_de_aluno_sem_selecao_devolve_lista_vazia_no_contrato()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var client = web.CreateStudentClient(await SeedStudentAsync(web));

        var response = await client.GetAsync("/api/user/inventory", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        Assert.Equal(0, JsonSerializer.Deserialize<JsonElement>(json).GetProperty("selected").GetArrayLength());
    }

    [Fact]
    public async Task Post_substitui_a_selecao_e_responde_com_a_gravada_igual_ao_get()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var client = web.CreateStudentClient(await SeedStudentAsync(web));

        var first = await client.PostAsJsonAsync("/api/user/inventory", new
        {
            selected = new[] { Item("cabelo-03", "Cacheado", "cabelo"), Item("roupa-01", "Explorador", "roupa") },
        }, Ct);
        var second = await client.PostAsJsonAsync("/api/user/inventory", new
        {
            selected = new[] { Item("cabelo-01", "Liso", "cabelo"), Item("rosto-02", "Sorriso", "rosto") },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var postJson = await second.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(postJson));
        var getJson = await client.GetStringAsync("/api/user/inventory", Ct);
        Assert.Equal(Pairs(postJson), Pairs(getJson));
        Assert.Equal(["cabelo:cabelo-01:Liso", "rosto:rosto-02:Sorriso"], Pairs(getJson)); // roupa antiga saiu
    }

    [Fact]
    public async Task Post_com_lista_vazia_limpa_a_selecao()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var client = web.CreateStudentClient(await SeedStudentAsync(web));
        await client.PostAsJsonAsync("/api/user/inventory", new { selected = new[] { Item("pele-01", "Tom 1", "pele") } }, Ct);

        var response = await client.PostAsJsonAsync("/api/user/inventory", new { selected = Array.Empty<object>() }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Pairs(await client.GetStringAsync("/api/user/inventory", Ct)));
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        """{"selected":[{"id":"cabelo-01","name":"Liso","type":"cabelo"},{"id":"cabelo-02","name":"Crespo","type":"cabelo"}]}""",
        """{"selected":[{"id":"x-01","name":"Tipo estranho","type":"chapeu"}]}""",
        """{"selected":[{"id":"x","name":"a","type":"cabelo"},{"id":"x","name":"a","type":"pele"},{"id":"x","name":"a","type":"roupa"},{"id":"x","name":"a","type":"rosto"},{"id":"x","name":"a","type":"acessorio"},{"id":"x","name":"a","type":"cabelo"}]}""",
        """{"selected":[{"id":"","name":"Sem id","type":"cabelo"}]}""",
        """{"selected":[{"id":"cabelo-01","name":"Sem tipo"}]}""",
        """{"selected":[null]}""",
        """{"selected":null}""",
        """{}""",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Post_invalido_responde_400_e_nao_altera_a_selecao(string body)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var client = web.CreateStudentClient(await SeedStudentAsync(web));
        await client.PostAsJsonAsync("/api/user/inventory", new { selected = new[] { Item("pele-01", "Tom 1", "pele") } }, Ct);

        var response = await client.PostAsync("/api/user/inventory", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("invalid_inventory", error.GetProperty("error").GetString());
        Assert.Equal(["pele:pele-01:Tom 1"], Pairs(await client.GetStringAsync("/api/user/inventory", Ct)));
    }

    [Fact]
    public async Task Aluno_nunca_altera_o_inventario_de_outro()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var ana = web.CreateStudentClient(await SeedStudentAsync(web));
        using var bia = web.CreateStudentClient(await SeedStudentAsync(web));
        await ana.PostAsJsonAsync("/api/user/inventory", new { selected = new[] { Item("cabelo-03", "Cacheado", "cabelo") } }, Ct);

        await bia.PostAsJsonAsync("/api/user/inventory", new { selected = new[] { Item("cabelo-05", "Trança", "cabelo") } }, Ct);

        Assert.Equal(["cabelo:cabelo-03:Cacheado"], Pairs(await ana.GetStringAsync("/api/user/inventory", Ct)));
        Assert.Equal(["cabelo:cabelo-05:Trança"], Pairs(await bia.GetStringAsync("/api/user/inventory", Ct)));
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());
        var body = JsonContent.Create(new { selected = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/user/inventory", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/user/inventory", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/user/inventory", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.PostAsync("/api/user/inventory", body, Ct)).StatusCode);
    }

    private static string[] Pairs(string json) =>
        [.. JsonSerializer.Deserialize<JsonElement>(json).GetProperty("selected").EnumerateArray()
            .Select(i => $"{i.GetProperty("type").GetString()}:{i.GetProperty("id").GetString()}:{i.GetProperty("name").GetString()}")];

    private static async Task<Guid> SeedStudentAsync(WebApplicationFactory<Program> web)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = new Unit($"Unidade {suffix}");
        var user = new AppUser { UserName = $"aluno{suffix}", UnitId = unit.Id };
        db.Users.Add(user);
        db.Units.Add(unit);
        var student = new Student(
            user.Id, unit.Id, $"P-{suffix}", "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        db.Students.Add(student);
        await db.SaveChangesAsync(Ct);
        return student.Id;
    }
}
