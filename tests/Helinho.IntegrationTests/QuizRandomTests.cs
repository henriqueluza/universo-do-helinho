using System.Net;
using System.Text.Json;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>GET /api/quiz/random (P8): sessão no servidor, pergunta corrente estável, sem vazar a correta.</summary>
[Collection(PostgresCollection.Name)]
public class QuizRandomTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Devolve_pergunta_aprovada_do_tipo_e_faixa_sem_vazar_a_correta_e_passa_pelo_contrato()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        var band = await BandAsync(web, ana);
        await AddQuestionsAsync(web, QuizType.Ambiental, band, 3);
        await AddQuestionsAsync(web, QuizType.Cultural, band, 2);
        await AddQuestionsAsync(web, QuizType.Ambiental, OtherBand(band), 2);
        await AddQuestionsAsync(web, QuizType.Ambiental, band, 2, approve: false);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/quiz/random?quiz_type=ambiental", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(
            ["question_id", "quiz_type", "statement", "options", "position", "total", "time_limit"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ambiental", body.GetProperty("quiz_type").GetString());
        Assert.StartsWith("Ambiental", body.GetProperty("statement").GetString());
        Assert.EndsWith("aprovada", body.GetProperty("statement").GetString());
        Assert.Equal(1, body.GetProperty("position").GetInt32());
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(30, body.GetProperty("time_limit").GetInt32());
        var options = body.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(["a", "b", "c", "d"], options.Select(o => o.GetProperty("id").GetString()));
        Assert.All(options, o => Assert.Equal(["id", "label"], o.EnumerateObject().Select(p => p.Name)));
        Assert.DoesNotContain("correct", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explanation", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chamar_de_novo_sem_responder_devolve_a_mesma_pergunta_e_position()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Esportivo, await BandAsync(web, ana), 3);
        using var client = web.CreateStudentClient(ana);

        var first = await GetAsync(client, "esportivo");
        var second = await GetAsync(client, "esportivo");

        Assert.Equal(first.GetProperty("question_id").GetString(), second.GetProperty("question_id").GetString());
        Assert.Equal(1, second.GetProperty("position").GetInt32());
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.QuizSessions.CountAsync(s => s.StudentId == ana, Ct));
    }

    [Fact]
    public async Task Apos_responder_avanca_sem_repetir_e_sessao_esgotada_abre_outra()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Conhecimento, await BandAsync(web, ana), 2);
        using var client = web.CreateStudentClient(ana);

        var seen = new List<string>();
        var q1 = await GetAsync(client, "conhecimento");
        seen.Add(q1.GetProperty("question_id").GetString()!);
        await AnswerAsync(web, ana, seen[0]);
        var q2 = await GetAsync(client, "conhecimento");
        seen.Add(q2.GetProperty("question_id").GetString()!);

        Assert.Equal(2, q2.GetProperty("position").GetInt32());
        Assert.Equal(2, seen.Distinct().Count());

        await AnswerAsync(web, ana, seen[1]);
        var q3 = await GetAsync(client, "conhecimento");

        Assert.Equal(1, q3.GetProperty("position").GetInt32());
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = await db.QuizSessions.Where(s => s.StudentId == ana).ToListAsync(Ct);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, s => s.CompletedAt != null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?quiz_type=")]
    [InlineData("?quiz_type=aleatorio")]
    public async Task Quiz_type_ausente_ou_invalido_responde_400_com_corpo_de_erro(string query)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        using var client = web.CreateStudentClient(await SeedStudentAsync(web));

        var response = await client.GetAsync("/api/quiz/random" + query, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        Assert.Equal("invalid_quiz_type", JsonSerializer.Deserialize<JsonElement>(json).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Sem_questoes_disponiveis_responde_404_com_codigo_e_nao_500()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Diversidade, await BandAsync(web, ana), 2, approve: false);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/quiz/random?quiz_type=diversidade", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal("no_questions", JsonSerializer.Deserialize<JsonElement>(json).GetProperty("error").GetString());
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

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quiz/random?quiz_type=ambiental", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/quiz/random?quiz_type=ambiental", Ct)).StatusCode);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string type)
    {
        var response = await client.GetAsync($"/api/quiz/random?quiz_type={type}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
    }

    private static AgeBand OtherBand(AgeBand band) => band == AgeBand.Age13To14 ? AgeBand.Age6To7 : AgeBand.Age13To14;

    private static async Task<AgeBand> BandAsync(WebApplicationFactory<Program> web, Guid studentId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var student = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == studentId, Ct);
        return student.AgeBandAt(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static async Task AnswerAsync(WebApplicationFactory<Program> web, Guid studentId, string questionId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.QuizSessions.Where(s => s.StudentId == studentId && s.CompletedAt == null).SingleAsync(Ct);
        db.QuizAnswers.Add(new QuizAnswer(session.Id, Guid.Parse(questionId), "a", true, 10, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
    }

    private static async Task AddQuestionsAsync(
        WebApplicationFactory<Program> web, QuizType type, AgeBand band, int count, bool approve = true)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < count; i++)
        {
            var q = new Question(
                type, band, $"{type} {i}: {(approve ? "aprovada" : "rascunho")}", "Explicação.",
                [("A", false), ("B", true), ("C", false), ("D", false)]);
            if (approve)
            {
                q.Approve();
            }

            db.Questions.Add(q);
        }

        await db.SaveChangesAsync(Ct);
    }

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
