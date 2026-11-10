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

/// <summary>GET /api/user/stats: desempenho por tipo e totais calculados sobre todas as respostas do aluno.</summary>
[Collection(PostgresCollection.Name)]
public class UserStatsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aluno_sem_respostas_recebe_zeros_nos_cinco_tipos()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web, []);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/user/stats", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(
            ["conhecimento", "ambiental", "esportivo", "diversidade", "cultural"],
            body.GetProperty("performance").EnumerateArray().Select(i => i.GetProperty("quiz_type").GetString()));
        Assert.All(body.GetProperty("performance").EnumerateArray(), i => Assert.Equal(0, i.GetProperty("percentage").GetDouble()));
        Assert.Equal(0, body.GetProperty("answered_questions").GetInt32());
        Assert.Equal(0, body.GetProperty("correct_average_questions").GetDouble());
    }

    [Fact]
    public async Task Totais_saem_de_todas_as_respostas_e_nao_da_media_das_categorias()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        // ambiental 1/1, conhecimento 1/4, esportivo 0/2 → geral 2/7; média das três categorias seria 0,4167.
        var ana = await SeedStudentAsync(web, [(QuizType.Ambiental, [true]), (QuizType.Conhecimento, [true, false, false, false]), (QuizType.Esportivo, [false, false])]);
        var other = await SeedStudentAsync(web, [(QuizType.Cultural, [true, true])]);
        using var client = web.CreateStudentClient(ana);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/user/stats", Ct);

        var byType = body.GetProperty("performance").EnumerateArray()
            .ToDictionary(i => i.GetProperty("quiz_type").GetString()!, i => i.GetProperty("percentage").GetDouble());
        Assert.Equal(1.0, byType["ambiental"]);
        Assert.Equal(0.25, byType["conhecimento"]);
        Assert.Equal(0.0, byType["esportivo"]);
        Assert.Equal(0.0, byType["cultural"]); // respostas de outro aluno não entram
        Assert.All(byType.Values, v => Assert.InRange(v, 0, 1));
        Assert.Equal(7, body.GetProperty("answered_questions").GetInt32());
        Assert.Equal(2.0 / 7, body.GetProperty("correct_average_questions").GetDouble(), 10);

        using var otherClient = web.CreateStudentClient(other);
        var otherBody = await otherClient.GetFromJsonAsync<JsonElement>("/api/user/stats", Ct);
        Assert.Equal(2, otherBody.GetProperty("answered_questions").GetInt32());
        Assert.Equal(1.0, otherBody.GetProperty("correct_average_questions").GetDouble());
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/user/stats", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/user/stats", Ct)).StatusCode);
    }

    private static async Task<Guid> SeedStudentAsync(
        WebApplicationFactory<Program> web, (QuizType Type, bool[] Results)[] sessions)
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
        var now = DateTimeOffset.UtcNow;
        foreach (var (type, results) in sessions)
        {
            var session = new QuizSession(student.Id, type, results.Length, now);
            db.QuizSessions.Add(session);
            for (var i = 0; i < results.Length; i++)
            {
                var q = new Question(
                    type, AgeBand.Age8To10, $"Pergunta {i}?", "Explicação.",
                    [("A", true), ("B", false), ("C", false), ("D", false)]);
                db.Questions.Add(q);
                db.QuizAnswers.Add(new QuizAnswer(session.Id, q.Id, results[i] ? "A" : "B", results[i], results[i] ? 10 : 0, now));
            }
        }

        await db.SaveChangesAsync(Ct);
        return student.Id;
    }
}
