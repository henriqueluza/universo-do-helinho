using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>GET /api/profile com dados sintéticos: nível/XP, ausência de dados cadastrais e isolamento entre alunos.</summary>
[Collection(PostgresCollection.Name)]
public class ProfileTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Responde_nivel_e_xp_do_aluno_sem_dados_cadastrais_e_passa_pelo_verificador()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (ana, bia) = await SeedAsync(web, anaXp: 320, biaXp: 0);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/profile", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(
            ["display_name", "avatar", "level", "level_name", "current_xp", "next_level_xp"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Ana Fictícia", body.GetProperty("display_name").GetString());
        Assert.Equal(320, body.GetProperty("current_xp").GetInt32());
        Assert.Equal(LevelPolicy.LevelFor(320), body.GetProperty("level").GetInt32());
        Assert.Equal(LevelPolicy.LevelName(3), body.GetProperty("level_name").GetString());
        Assert.Equal(LevelPolicy.NextLevelXp(320), body.GetProperty("next_level_xp").GetInt32());
        Assert.Equal(600, body.GetProperty("next_level_xp").GetInt32());
        foreach (var secret in new[] { "000.000.000-00", "00.000.000-0", "Rua Sintética", "Responsável Fictício" })
        {
            Assert.DoesNotContain(secret, json);
        }

        using var other = web.CreateStudentClient(bia);
        var otherBody = await other.GetFromJsonAsync<JsonElement>("/api/profile", Ct);
        Assert.Equal("Bia Fictícia", otherBody.GetProperty("display_name").GetString());
        Assert.Equal(0, otherBody.GetProperty("current_xp").GetInt32());
        Assert.Equal(100, otherBody.GetProperty("next_level_xp").GetInt32());
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        await using (var scope = web.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(Ct);
        }

        using var anonymous = factory.CreateClient();
        using var unknown = web.CreateStudentClient(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/profile", Ct)).StatusCode);
    }

    private static async Task<(Guid Ana, Guid Bia)> SeedAsync(WebApplicationFactory<Program> web, int anaXp, int biaXp)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = new Unit($"Unidade {suffix}");
        var users = new[] { new AppUser { UserName = $"ana{suffix}", UnitId = unit.Id }, new AppUser { UserName = $"bia{suffix}", UnitId = unit.Id } };
        db.Users.AddRange(users);
        db.Units.Add(unit);
        var ana = NewStudent(users[0].Id, unit.Id, $"P-A{suffix}", "Ana Fictícia", anaXp);
        var bia = NewStudent(users[1].Id, unit.Id, $"P-B{suffix}", "Bia Fictícia", biaXp);
        db.Students.AddRange(ana, bia);
        await db.SaveChangesAsync(Ct);
        return (ana.Id, bia.Id);
    }

    private static Student NewStudent(Guid userId, Guid unitId, string enrollment, string name, int xp)
    {
        var student = new Student(
            userId, unitId, enrollment, name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        student.AddXp(xp);
        return student;
    }
}
