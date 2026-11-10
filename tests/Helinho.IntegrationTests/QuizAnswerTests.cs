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

/// <summary>POST /api/quiz/{question_id}/answer (P8, P13): XP só no acerto, idempotência, sessão e missões.</summary>
[Collection(PostgresCollection.Name)]
public class QuizAnswerTests(PostgresFixture postgres)
{
    // Faixa 11–12 só é usada aqui: o banco é compartilhado entre as classes e a pergunta corrente depende do pool.
    private static readonly DateOnly Birth = new(2014, 1, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Acerto_soma_xp_e_responde_o_contrato_completo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Ambiental, 3);
        using var client = web.CreateStudentClient(ana);

        var (status, json, body) = await AnswerCurrentAsync(client, "ambiental", "b");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(JsonUtilityContract.Verify(json));
        Assert.Equal(
            ["is_correct", "correct_option_id", "correct_answer_text", "explanation", "xp_gained", "streak_count", "current_xp", "next_level_xp", "has_next"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.True(body.GetProperty("is_correct").GetBoolean());
        Assert.Equal("b", body.GetProperty("correct_option_id").GetString());
        Assert.Equal("B", body.GetProperty("correct_answer_text").GetString());
        Assert.Equal("Explicação.", body.GetProperty("explanation").GetString());
        Assert.Equal(10, body.GetProperty("xp_gained").GetInt32());
        Assert.Equal(1, body.GetProperty("streak_count").GetInt32());
        Assert.Equal(10, body.GetProperty("current_xp").GetInt32());
        Assert.Equal(100, body.GetProperty("next_level_xp").GetInt32());
        Assert.True(body.GetProperty("has_next").GetBoolean());
    }

    [Fact]
    public async Task Erro_nao_tira_xp_zera_a_sequencia_e_revela_a_correta()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Cultural, 4);
        using var client = web.CreateStudentClient(ana);

        var first = (await AnswerCurrentAsync(client, "cultural", "b")).Body;
        var second = (await AnswerCurrentAsync(client, "cultural", "b")).Body;
        var wrong = (await AnswerCurrentAsync(client, "cultural", "a")).Body;
        var after = (await AnswerCurrentAsync(client, "cultural", "b")).Body;

        Assert.Equal(2, second.GetProperty("streak_count").GetInt32());
        Assert.False(wrong.GetProperty("is_correct").GetBoolean());
        Assert.Equal("b", wrong.GetProperty("correct_option_id").GetString());
        Assert.Equal(0, wrong.GetProperty("xp_gained").GetInt32());
        Assert.Equal(0, wrong.GetProperty("streak_count").GetInt32());
        Assert.Equal(20, wrong.GetProperty("current_xp").GetInt32());
        Assert.Equal(1, after.GetProperty("streak_count").GetInt32());
        Assert.Equal(30, after.GetProperty("current_xp").GetInt32());
        Assert.Equal(10, first.GetProperty("current_xp").GetInt32());
    }

    [Fact]
    public async Task Reenviar_devolve_o_mesmo_corpo_sem_somar_xp_de_novo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Esportivo, 3);
        using var client = web.CreateStudentClient(ana);
        var qid = await CurrentIdAsync(client, "esportivo");

        var first = await PostAsync(client, qid, "b");
        var second = await PostAsync(client, qid, "b");
        var different = await PostAsync(client, qid, "a"); // a primeira resposta vale

        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Json, second.Json);
        Assert.Equal(first.Json, different.Json);
        Assert.Equal(10, await XpAsync(web, ana));
    }

    [Fact]
    public async Task Requisicoes_simultaneas_da_mesma_resposta_somam_xp_uma_vez()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Conhecimento, 3);
        using var client = web.CreateStudentClient(ana);
        var qid = await CurrentIdAsync(client, "conhecimento");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => PostAsync(client, qid, "b"), Ct)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(results.Select(r => r.Json).Distinct());
        Assert.Equal(10, await XpAsync(web, ana));
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.QuizAnswers.CountAsync(a => a.QuestionId == Guid.Parse(qid), Ct));
    }

    [Fact]
    public async Task Pergunta_fora_da_corrente_ou_option_inexistente_respondem_erro_com_codigo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Diversidade, 3);
        using var client = web.CreateStudentClient(ana);
        var current = await CurrentIdAsync(client, "diversidade");
        string other;
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            other = (await db.Questions.Where(q => q.QuizType == QuizType.Diversidade && q.Id != Guid.Parse(current))
                .OrderByDescending(q => q.Id).FirstAsync(Ct)).Id.ToString();
        }

        var notCurrent = await PostAsync(client, other, "b");
        var unknown = await PostAsync(client, Guid.NewGuid().ToString(), "b");
        var badFormat = await PostAsync(client, "q-1", "b");
        var badOption = await PostAsync(client, current, "z");
        var noOption = await PostAsync(client, current, null);

        Assert.All([notCurrent, unknown, badFormat], r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        Assert.All([notCurrent, unknown, badFormat], r => Assert.Equal("question_not_current", r.Body.GetProperty("error").GetString()));
        Assert.All([badOption, noOption], r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.All([badOption, noOption], r => Assert.Equal("invalid_option", r.Body.GetProperty("error").GetString()));
        Assert.Empty(JsonUtilityContract.Verify(notCurrent.Json));
        Assert.Equal(0, await XpAsync(web, ana));
    }

    [Fact]
    public async Task Ultima_pergunta_tem_has_next_falso_encerra_a_sessao_e_o_reenvio_repete_o_corpo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Ambiental, 2);
        using var client = web.CreateStudentClient(ana);

        var first = (await AnswerCurrentAsync(client, "ambiental", "b")).Body;
        var lastId = await CurrentIdAsync(client, "ambiental");
        var last = await PostAsync(client, lastId, "b");
        var replay = await PostAsync(client, lastId, "b");

        Assert.True(first.GetProperty("has_next").GetBoolean());
        Assert.False(last.Body.GetProperty("has_next").GetBoolean());
        Assert.Equal(last.Json, replay.Json);
        Assert.Equal(20, await XpAsync(web, ana));
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.All(await db.QuizSessions.Where(s => s.StudentId == ana).ToListAsync(Ct), s => Assert.NotNull(s.CompletedAt));
    }

    [Fact]
    public async Task Atualiza_o_progresso_das_missoes_genericas_e_do_tipo_na_mesma_transacao()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var ana = await SeedStudentAsync(web);
        await AddQuestionsAsync(web, QuizType.Cultural, 3);
        var (generic, cultural, esportivo) = await AddQuestsAsync(web);
        using var client = web.CreateStudentClient(ana);
        var qid = await CurrentIdAsync(client, "cultural");

        await PostAsync(client, qid, "b");
        await PostAsync(client, qid, "b"); // reenvio não soma
        await AnswerCurrentAsync(client, "cultural", "a");

        await using var scope = web.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().QuestProgress
            .Where(p => p.StudentId == ana).ToListAsync(Ct);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Single(r => r.QuestId == generic).Current);
        Assert.Equal(1, rows.Single(r => r.QuestId == cultural).Current); // meta 1: não passa dela
        Assert.DoesNotContain(rows, r => r.QuestId == esportivo);
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
        var path = $"/api/quiz/{Guid.NewGuid()}/answer";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, new { option_id = "a" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.PostAsJsonAsync(path, new { option_id = "a" }, Ct)).StatusCode);
    }

    private sealed record Posted(HttpStatusCode Status, string Json, JsonElement Body);

    private static async Task<Posted> PostAsync(HttpClient client, string questionId, string? option)
    {
        var response = await client.PostAsJsonAsync($"/api/quiz/{questionId}/answer", new { option_id = option }, Ct);
        var json = await response.Content.ReadAsStringAsync(Ct);
        return new Posted(response.StatusCode, json, JsonSerializer.Deserialize<JsonElement>(json));
    }

    private static async Task<string> CurrentIdAsync(HttpClient client, string type)
    {
        var response = await client.GetAsync($"/api/quiz/random?quiz_type={type}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct))
            .GetProperty("question_id").GetString()!;
    }

    private static async Task<Posted> AnswerCurrentAsync(HttpClient client, string type, string option) =>
        await PostAsync(client, await CurrentIdAsync(client, type), option);

    private static async Task<int> XpAsync(WebApplicationFactory<Program> web, Guid studentId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Students.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == studentId, Ct)).Xp;
    }

    private static async Task<(Guid Generic, Guid Cultural, Guid Esportivo)> AddQuestsAsync(WebApplicationFactory<Program> web)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generic = new DailyQuest("Responder 5 perguntas", 5, 30, null);
        var cultural = new DailyQuest("Jogar no Ateliê", 1, 40, QuizType.Cultural);
        var esportivo = new DailyQuest("Jogar no Estádio", 1, 40, QuizType.Esportivo);
        db.DailyQuests.AddRange(generic, cultural, esportivo);
        await db.SaveChangesAsync(Ct);
        return (generic.Id, cultural.Id, esportivo.Id);
    }

    private static async Task AddQuestionsAsync(WebApplicationFactory<Program> web, QuizType type, int count)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var band = AgeBands.FromBirthDate(Birth, DateOnly.FromDateTime(DateTime.UtcNow));
        for (var i = 0; i < count; i++)
        {
            var q = new Question(type, band, $"{type} {i}", "Explicação.", [("A", false), ("B", true), ("C", false), ("D", false)]);
            q.Approve();
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
            Birth, "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        db.Students.Add(student);
        await db.SaveChangesAsync(Ct);
        return student.Id;
    }
}
