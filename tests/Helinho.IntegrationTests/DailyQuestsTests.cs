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

/// <summary>GET /api/quests/daily e /daily-bonus: contrato, P6 (quiz_type ""), progresso do dia e virada em Brasília.</summary>
[Collection(PostgresCollection.Name)]
public class DailyQuestsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missoes_trazem_campos_do_contrato_progresso_do_dia_e_quiz_type_vazio_na_generica()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (ana, other) = await SeedAsync(web);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/quests/daily", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(["renews_at", "quests"], body.EnumerateObject().Select(p => p.Name));
        var quests = body.GetProperty("quests").EnumerateArray().ToList();
        Assert.All(
            quests,
            q => Assert.Equal(
                ["id", "title", "current", "total", "reward_xp", "quiz_type"], q.EnumerateObject().Select(p => p.Name)));
        var byTitle = quests.ToDictionary(q => q.GetProperty("title").GetString()!);
        Assert.Equal(3, byTitle.Count);

        var generic = byTitle["Responder 5 perguntas"];
        Assert.Equal(JsonValueKind.String, generic.GetProperty("quiz_type").ValueKind); // P6: nunca null
        Assert.Equal("", generic.GetProperty("quiz_type").GetString());
        Assert.Equal((3, 5, 30), Numbers(generic));
        Assert.Equal((0, 3, 50), Numbers(byTitle["Acertar 3 seguidas"])); // sem registro hoje
        Assert.Equal((0, 1, 40), Numbers(byTitle["Jogar no Ateliê da Luna"])); // progresso de ontem não conta
        Assert.Equal("cultural", byTitle["Jogar no Ateliê da Luna"].GetProperty("quiz_type").GetString());

        using var otherClient = web.CreateStudentClient(other);
        var otherBody = await otherClient.GetFromJsonAsync<JsonElement>("/api/quests/daily", Ct);
        Assert.All(otherBody.GetProperty("quests").EnumerateArray(), q => Assert.Equal(0, q.GetProperty("current").GetInt32()));
    }

    [Fact]
    public async Task Renews_at_e_a_proxima_meia_noite_de_Brasilia_em_utc()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (ana, _) = await SeedAsync(web);
        using var client = web.CreateStudentClient(ana);

        var before = DateTimeOffset.UtcNow;
        var body = await client.GetFromJsonAsync<JsonElement>("/api/quests/daily", Ct);
        var after = DateTimeOffset.UtcNow;

        var raw = body.GetProperty("renews_at").GetString()!;
        Assert.EndsWith("Z", raw);
        var renews = DateTimeOffset.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(new TimeSpan(3, 0, 0), renews.TimeOfDay); // 00:00 em Brasília (UTC−3)
        Assert.True(renews > before && renews <= after.AddDays(1));
    }

    [Fact]
    public async Task Bonus_diario_traz_reward_xp_e_required_quests()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/quests/daily-bonus", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(["reward_xp", "required_quests"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(100, body.GetProperty("reward_xp").GetInt32());
        Assert.Equal(3, body.GetProperty("required_quests").GetInt32());
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quests/daily", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quests/daily-bonus", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/quests/daily", Ct)).StatusCode);
    }

    private static (int Current, int Total, int Reward) Numbers(JsonElement q) =>
        (q.GetProperty("current").GetInt32(), q.GetProperty("total").GetInt32(), q.GetProperty("reward_xp").GetInt32());

    private static async Task<(Guid Ana, Guid Other)> SeedAsync(WebApplicationFactory<Program> web)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        await db.QuestProgress.ExecuteDeleteAsync(Ct);
        await db.DailyQuests.ExecuteDeleteAsync(Ct);

        var q1 = new DailyQuest("Responder 5 perguntas", 5, 30, null);
        var q2 = new DailyQuest("Acertar 3 seguidas", 3, 50, null);
        var q3 = new DailyQuest("Jogar no Ateliê da Luna", 1, 40, QuizType.Cultural);
        db.DailyQuests.AddRange(q1, q2, q3);

        var ana = NewStudent(db);
        var other = NewStudent(db);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddHours(-3).DateTime);
        db.QuestProgress.AddRange(
            new QuestProgress(ana.Id, q1.Id, today, 3),
            new QuestProgress(ana.Id, q3.Id, today.AddDays(-1), 1));
        await db.SaveChangesAsync(Ct);
        return (ana.Id, other.Id);
    }

    private static Student NewStudent(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = new Unit($"Unidade {suffix}");
        var user = new AppUser { UserName = $"aluno{suffix}", UnitId = unit.Id };
        db.Users.Add(user);
        db.Units.Add(unit);
        var student = new Student(
            user.Id, unit.Id, $"P-{suffix}", "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        db.Students.Add(student);
        return student;
    }
}
