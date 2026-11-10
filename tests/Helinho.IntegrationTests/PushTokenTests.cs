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

/// <summary>POST /api/devices/push-token (P9): 204, idempotente, token migra entre alunos, validação com 400.</summary>
[Collection(PostgresCollection.Name)]
public class PushTokenTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Post_grava_para_o_aluno_autenticado_e_responde_204_sem_corpo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (studentId, userId) = await SeedStudentAsync(web);
        using var client = web.CreateStudentClient(studentId);

        var response = await client.PostAsJsonAsync("/api/devices/push-token", new { token = "tok-a-" + userId, platform = "android" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
        var saved = Assert.Single(await RowsAsync(web, userId));
        Assert.Equal(("tok-a-" + userId, "android"), (saved.Token, saved.Platform));
    }

    [Fact]
    public async Task Registrar_o_mesmo_token_de_novo_nao_duplica()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (studentId, userId) = await SeedStudentAsync(web);
        using var client = web.CreateStudentClient(studentId);
        var body = new { token = "tok-b-" + userId, platform = "ios" };

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/devices/push-token", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/devices/push-token", body, Ct)).StatusCode);

        Assert.Single(await RowsAsync(web, userId));
    }

    [Fact]
    public async Task Token_que_era_de_outro_aluno_passa_para_o_atual()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (anaId, anaUser) = await SeedStudentAsync(web);
        var (biaId, biaUser) = await SeedStudentAsync(web);
        var token = "tablet-" + Guid.NewGuid();
        using var ana = web.CreateStudentClient(anaId);
        using var bia = web.CreateStudentClient(biaId);

        await ana.PostAsJsonAsync("/api/devices/push-token", new { token, platform = "android" }, Ct);
        var response = await bia.PostAsJsonAsync("/api/devices/push-token", new { token, platform = "android" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await RowsAsync(web, anaUser));
        Assert.Equal(token, Assert.Single(await RowsAsync(web, biaUser)).Token);
    }

    public static TheoryData<string> InvalidBodies => new()
    {
        """{"token":"abc","platform":"windows"}""",
        """{"token":"abc","platform":"Android"}""",
        """{"token":"abc"}""",
        """{"token":"","platform":"ios"}""",
        """{"token":"   ","platform":"ios"}""",
        """{"platform":"ios"}""",
        """{"token":null,"platform":null}""",
        "{}",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Corpo_invalido_responde_400_e_nao_grava(string body)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (studentId, userId) = await SeedStudentAsync(web);
        using var client = web.CreateStudentClient(studentId);

        var response = await client.PostAsync("/api/devices/push-token", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        Assert.Equal("invalid_push_token", JsonSerializer.Deserialize<JsonElement>(json).GetProperty("error").GetString());
        Assert.Empty(await RowsAsync(web, userId));
    }

    [Fact]
    public async Task Token_acima_de_512_caracteres_responde_400_e_o_limite_e_aceito()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (studentId, userId) = await SeedStudentAsync(web);
        using var client = web.CreateStudentClient(studentId);

        var tooLong = await client.PostAsJsonAsync("/api/devices/push-token", new { token = new string('x', 513), platform = "ios" }, Ct);
        var atLimit = await client.PostAsJsonAsync("/api/devices/push-token", new { token = new string('y', 512), platform = "ios" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, atLimit.StatusCode);
        Assert.Single(await RowsAsync(web, userId));
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());
        var body = JsonContent.Create(new { token = "abc", platform = "ios" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/devices/push-token", body, Ct)).StatusCode);
        var forbidden = await unknown.PostAsync("/api/devices/push-token", JsonContent.Create(new { token = "abc", platform = "ios" }), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty(JsonUtilityContract.Verify(await forbidden.Content.ReadAsStringAsync(Ct)));
    }

    private static async Task<List<PushToken>> RowsAsync(WebApplicationFactory<Program> web, Guid userId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PushTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync(Ct);
    }

    private static async Task<(Guid StudentId, Guid UserId)> SeedStudentAsync(WebApplicationFactory<Program> web)
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
        return (student.Id, user.Id);
    }
}
