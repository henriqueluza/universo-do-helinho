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

/// <summary>GET /api/quizzes/progress: cinco tipos sempre, estados (PROPOSTA) e sessão corrente do aluno.</summary>
[Collection(PostgresCollection.Name)]
public class QuizProgressTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_historico_traz_os_cinco_tipos_com_new_e_diversidade_locked()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedAsync(web, xp: 0);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/quizzes/progress", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var items = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("progress").EnumerateArray().ToList();
        Assert.Equal(
            ["ambiental", "conhecimento", "esportivo", "cultural", "diversidade"],
            items.Select(i => i.GetProperty("quiz_type").GetString()));
        Assert.All(items, i => Assert.Equal(["quiz_type", "current", "total", "state"], i.EnumerateObject().Select(p => p.Name)));
        Assert.All(items, i => Assert.Equal(0, i.GetProperty("current").GetInt32()));
        Assert.Equal(
            ["new", "new", "new", "new", "locked"],
            items.Select(i => i.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Sessao_no_meio_reflete_current_e_total_e_nao_vaza_para_outro_aluno()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedAsync(web, xp: 150);
        var other = await AddSessionsAsync(web, ana);
        using var client = web.CreateStudentClient(ana);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/quizzes/progress", Ct);

        var byType = body.GetProperty("progress").EnumerateArray().ToDictionary(i => i.GetProperty("quiz_type").GetString()!);
        Assert.Equal(("in_progress", 3, 5), State(byType["ambiental"]));
        Assert.Equal(("not_started", 0, 10), State(byType["cultural"]));
        Assert.Equal(("new", 0, 10), State(byType["esportivo"]));
        Assert.Equal(("new", 0, 10), State(byType["diversidade"])); // nível 2 já libera Diversidade

        using var otherClient = web.CreateStudentClient(other);
        var otherBody = await otherClient.GetFromJsonAsync<JsonElement>("/api/quizzes/progress", Ct);
        Assert.All(
            otherBody.GetProperty("progress").EnumerateArray(),
            i => Assert.NotEqual("in_progress", i.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync(); // banco próprio por fábrica: precisa do esquema
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quizzes/progress", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/quizzes/progress", Ct)).StatusCode);
    }

    private static (string State, int Current, int Total) State(JsonElement item) =>
        (item.GetProperty("state").GetString()!, item.GetProperty("current").GetInt32(), item.GetProperty("total").GetInt32());

    private static async Task<Guid> SeedAsync(WebApplicationFactory<Program> web, int xp)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        var student = NewStudent(db, xp);
        await db.SaveChangesAsync(Ct);
        return student.Id;
    }

    /// <summary>Ana: ambiental aberta com 3 de 5 respondidas, cultural concluída. Outro aluno sem sessões.</summary>
    private static async Task<Guid> AddSessionsAsync(WebApplicationFactory<Program> web, Guid ana)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var open = new QuizSession(ana, QuizType.Ambiental, 5, now);
        var done = new QuizSession(ana, QuizType.Cultural, 10, now.AddHours(-1));
        db.Entry(done).Property(s => s.CompletedAt).CurrentValue = now.AddMinutes(-30);
        db.QuizSessions.AddRange(open, done);
        await db.SaveChangesAsync(Ct);
        for (var i = 0; i < 3; i++)
        {
            var q = new Question(
                QuizType.Ambiental, AgeBand.Age8To10, $"Pergunta {i}?", "Explicação.",
                [("A", true), ("B", false), ("C", false), ("D", false)]);
            db.Questions.Add(q);
            db.QuizAnswers.Add(new QuizAnswer(open.Id, q.Id, "A", true, 10, now));
        }

        var other = NewStudent(db, 0);
        await db.SaveChangesAsync(Ct);
        return other.Id;
    }

    private static Student NewStudent(AppDbContext db, int xp)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = new Unit($"Unidade {suffix}");
        var user = new AppUser { UserName = $"aluno{suffix}", UnitId = unit.Id };
        db.Users.Add(user);
        db.Units.Add(unit);
        var student = new Student(
            user.Id, unit.Id, $"P-{suffix}", "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        student.AddXp(xp);
        db.Students.Add(student);
        return student;
    }
}
