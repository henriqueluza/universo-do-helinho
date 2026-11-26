using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Ai;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #259: o tratador do trabalho de geração (ADR-0004 §10.2). Sem host web e sem o trabalhador da fila: o teste
/// faz o papel do processador, chamando o tratador e depois a gravação única.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AiProcessDraftJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly string[] FourOptions = ["Um", "Dois", "Três", "Quatro"];
    private readonly string _database = $"ia_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now.AddSeconds(30);
    }

    /// <summary>Gerador de teste: guarda o que recebeu e devolve o lote combinado, lança a falha ou espera o cancelamento.</summary>
    private sealed class StubGenerator(IReadOnlyList<QuestionDraft>? drafts = null, QuestionDraftFailure? failure = null) : IQuestionDraftGenerator
    {
        public List<QuestionDraftRequest> Requests { get; } = [];

        public async Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (drafts is null && failure is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return failure is { } reason
                ? throw new QuestionDraftGenerationException(reason)
                : new QuestionDraftBatch(drafts ?? [], QuestionPromptBuilder.Version,
                    new AiUsage("Stub", "modelo-sintetico", 11, 22, TimeSpan.FromSeconds(3), "req_sintetico-01"));
        }
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new AnonymousCurrentUser());

    private static ProcessAiDraftJob Handler(AppDbContext db, IQuestionDraftGenerator generator, int timeoutSeconds = 90) =>
        new(db, generator, new FixedClock(), Options.Create(new AiOptions { Provider = AiOptions.Fake, TimeoutSeconds = timeoutSeconds }));

    private static JobContext Job(Guid aiJobId) =>
        new(Guid.NewGuid(), RequestAiDrafts.JobType, Ana, JsonDocument.Parse($$"""{"v":1,"aiJobId":"{{aiJobId}}"}""").RootElement, 1, Now, Now.AddMinutes(10));

    private static QuestionDraft Draft(string statement, params string[] options) => new(statement, options.Length == 0 ? FourOptions : options, 0, "Explicação sintética.");

    private async Task<AiGenerationJob> SeedAsync(int count = 5, QuizType type = QuizType.Cultural, string? subject = "Festas juninas")
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var request = new AiGenerationJob(AiJobKind.Batch, type, AgeBand.Age8To10, count, subject, Ana, Now);
        db.AiGenerationJobs.Add(request);
        await db.SaveChangesAsync(Ct);
        return request;
    }

    private async Task<(AiGenerationJob Request, List<AiQuestionSuggestion> Suggestions)> ReadAsync(Guid id)
    {
        await using var db = NewContext();
        return (await db.AiGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == id, Ct),
            await db.AiQuestionSuggestions.AsNoTracking().Where(s => s.JobId == id).OrderBy(s => s.Position).ToListAsync(Ct));
    }

    [Fact]
    public void Politica_da_raia_tem_2_tentativas_lote_1_e_posse_maior_que_o_prazo_da_chamada()
    {
        using var db = NewContext();
        var standard = Handler(db, new StubGenerator());
        var policy = Handler(db, new StubGenerator(), timeoutSeconds: 30).Policy;

        Assert.Equal([RequestAiDrafts.JobType], standard.Types);
        Assert.Equal((2, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150)),
            (standard.Policy.MaxAttempts, standard.Policy.BatchSize, standard.Policy.ItemTimeout, standard.Policy.Lease));
        Assert.Equal((TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)), (policy.ItemTimeout, policy.Lease));
        Assert.True(policy.BatchSize * policy.ItemTimeout <= policy.Lease);
    }

    [Fact]
    public async Task Sucesso_deixa_sugestoes_uso_e_contadores_no_contexto_e_so_a_gravacao_do_processador_os_grava()
    {
        var seeded = await SeedAsync();
        await using var db = NewContext();

        var result = await Handler(db, new FakeQuestionDraftGenerator(Options.Create(new FakeAiOptions()))).HandleAsync(Job(seeded.Id), Ct);

        var before = await ReadAsync(seeded.Id);
        Assert.IsType<JobResult.Succeeded>(result);
        Assert.Equal((AiJobStatus.Processing, 0), (before.Request.Status, before.Suggestions.Count)); // só a marcação "gerando" foi gravada
        await db.SaveChangesAsync(Ct); // a gravação única do processador
        var (request, suggestions) = await ReadAsync(seeded.Id);
        Assert.Equal((AiJobStatus.Succeeded, "Fake", "fake-v1", QuestionPromptBuilder.Version), (request.Status, request.Provider, request.Model, request.PromptVersion));
        Assert.Equal((5, 5, 0, 0), (request.GeneratedCount, request.ValidCount, request.InvalidCount, request.DuplicateCount));
        Assert.True(request.InputTokens > 0 && request.OutputTokens > 0 && request.FinishedAt == Now.AddSeconds(30));
        Assert.Equal([0, 1, 2, 3, 4], suggestions.Select(s => s.Position));
        Assert.All(suggestions, s => Assert.Equal(AiSuggestionStatus.Pending, s.Status));
        Assert.Equal(0, await db.Questions.CountAsync(Ct)); // nada gerado vira questão sem uma pessoa
    }

    [Fact]
    public async Task Rascunho_invalido_ou_duplicado_e_contado_e_nao_e_gravado_e_ao_gerador_so_vao_enunciados_de_questoes()
    {
        var seeded = await SeedAsync(count: 6);
        await using (var seed = NewContext())
        {
            var earlier = new AiGenerationJob(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age8To10, 1, null, Ana, Now.AddHours(-1));
            seed.AddRange(
                new Question(QuizType.Cultural, AgeBand.Age8To10, "Qual é a questão sintética já existente?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]),
                new Question(QuizType.Cultural, AgeBand.Age13To14, "Qual é a questão sintética de outra faixa?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]),
                earlier, new AiQuestionSuggestion(earlier.Id, 0, "Qual é a sugestão sintética ainda guardada?", FourOptions, 0, "Explicação."));
            await seed.SaveChangesAsync(Ct);
        }

        var generator = new StubGenerator([
            Draft("Qual é o rascunho sintético válido?"), Draft("Qual é o <script>alert(1)</script> rascunho?"), Draft("Qual tem três alternativas?", "Um", "Dois", "Três"),
            Draft("QUAL E A QUESTAO SINTETICA JA EXISTENTE"), Draft("Qual é a sugestão sintética ainda guardada?"), QuestionDraftOutputParser.MalformedItem]);
        await using var db = NewContext();
        var result = await Handler(db, generator).HandleAsync(Job(seeded.Id), Ct);
        await db.SaveChangesAsync(Ct);

        var (request, suggestions) = await ReadAsync(seeded.Id);
        Assert.Equal("req_sintetico-01", Assert.IsType<JobResult.Succeeded>(result).ExternalId);
        Assert.Equal((AiJobStatus.Succeeded, 6, 1, 3, 2), (request.Status, request.GeneratedCount, request.ValidCount, request.InvalidCount, request.DuplicateCount));
        Assert.Equal("Qual é o rascunho sintético válido?", Assert.Single(suggestions).Statement);
        var sent = Assert.Single(generator.Requests); // §11: tipo, faixa, quantidade, assunto e enunciados de Questions; nada de quem pediu
        Assert.Equal((QuizType.Cultural, AgeBand.Age8To10, 6, "Festas juninas"), (sent.QuizType, sent.AgeBand, sent.Count, sent.Subject));
        Assert.Equal(["Qual é a questão sintética já existente?"], sent.ExistingStatements);
    }

    [Theory]
    [InlineData(QuestionDraftFailure.Timeout, "ai_timeout")]
    [InlineData(QuestionDraftFailure.ProviderUnavailable, "ai_unavailable")]
    [InlineData(QuestionDraftFailure.RateLimited, "ai_rate_limited")]
    [InlineData(QuestionDraftFailure.Refused, "ai_refused")]
    [InlineData(QuestionDraftFailure.InvalidOutput, "ai_invalid_output")]
    [InlineData(QuestionDraftFailure.Misconfigured, "ai_misconfigured")]
    [InlineData(QuestionDraftFailure.Disabled, "ai_disabled")]
    [InlineData(null, "ai_timeout")] // o gerador não responde e o processador cancela o token no tempo máximo do item
    public async Task Falha_do_gerador_vira_falha_com_o_codigo_fechado_no_pedido_e_na_fila_sem_repetir(QuestionDraftFailure? failure, string code)
    {
        var seeded = await SeedAsync();
        await using var db = NewContext();
        using var itemTimeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        itemTimeout.CancelAfter(failure is null ? TimeSpan.FromMilliseconds(200) : Timeout.InfiniteTimeSpan);

        var result = await Handler(db, new StubGenerator(failure: failure)).HandleAsync(Job(seeded.Id), itemTimeout.Token);
        await db.SaveChangesAsync(Ct);

        var (request, suggestions) = await ReadAsync(seeded.Id);
        Assert.Equal(code, Assert.IsType<JobResult.Fail>(result).ErrorCode); // nunca Retry nem Suspend (§10.2)
        Assert.Equal((AiJobStatus.Failed, code, 0), (request.Status, request.FailureReason, suggestions.Count));
    }

    [Fact]
    public async Task Pedido_ja_concluido_devolve_sucesso_sem_chamar_o_gerador_e_trabalho_sem_pedido_falha()
    {
        var seeded = await SeedAsync();
        await using var db = NewContext();
        (await db.AiGenerationJobs.SingleAsync(Ct)).Fail("expired", Now.AddMinutes(11));
        await db.SaveChangesAsync(Ct);
        var generator = new StubGenerator([Draft("Qual é o rascunho sintético que não deve ser pedido?")]);
        var handler = Handler(db, generator);

        var recaptured = await handler.HandleAsync(Job(seeded.Id), Ct);
        var missing = await handler.HandleAsync(Job(Guid.NewGuid()), Ct);
        var malformed = await handler.HandleAsync(Job(seeded.Id) with { Payload = JsonDocument.Parse("""{"v":1,"aiJobId":7}""").RootElement }, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.IsType<JobResult.Succeeded>(recaptured);
        Assert.Equal([AiFailureCodes.JobMissing, AiFailureCodes.JobMissing], new[] { missing, malformed }.Select(r => Assert.IsType<JobResult.Fail>(r).ErrorCode));
        Assert.Empty(generator.Requests);
        var request = (await ReadAsync(seeded.Id)).Request;
        Assert.Equal((AiJobStatus.Failed, "expired"), (request.Status, request.FailureReason));
    }

    [Fact]
    public async Task Encerramento_pela_fila_marca_o_pedido_nao_concluido_como_falho_sem_salvar_e_nao_muda_o_concluido()
    {
        var seeded = await SeedAsync();
        await using var db = NewContext();
        var handler = Handler(db, new StubGenerator());

        await handler.OnAbandonedAsync(Job(seeded.Id), "max_attempts", Ct);
        Assert.Equal(AiJobStatus.Pending, (await ReadAsync(seeded.Id)).Request.Status); // quem grava é o processador, junto com o Failed da fila
        await db.SaveChangesAsync(Ct);
        await handler.OnAbandonedAsync(Job(seeded.Id), "expired", Ct); // já concluído: não muda
        await handler.OnAbandonedAsync(Job(Guid.NewGuid()), "expired", Ct); // sem pedido: não faz nada
        await db.SaveChangesAsync(Ct);

        var request = (await ReadAsync(seeded.Id)).Request;
        Assert.Equal((AiJobStatus.Failed, "max_attempts", Now.AddSeconds(30)), (request.Status, request.FailureReason, request.FinishedAt));
    }
}
