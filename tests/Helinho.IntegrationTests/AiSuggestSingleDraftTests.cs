using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #261: sugestão avulsa no criador e proveniência ao salvar (ADR-0004 §9.3, §13 e §15). Sem host web.</summary>
[Collection(PostgresCollection.Name)]
public class AiSuggestSingleDraftTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bia = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string[] FourOptions = ["Um", "Dois", "Três", "Quatro"];
    private readonly string _database = $"ia_{Guid.NewGuid():N}"; // um banco por teste
    private readonly TestClock _clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = T0;
    }

    private sealed record TestUser(Guid? UserId, string? Role) : ICurrentUser
    {
        public bool IsAuthenticated => UserId is not null;
        public Guid? UnitId => null;
        public bool IsInRole(string role) => role == Role;
    }

    /// <summary>Devolve o rascunho combinado, lança a falha ou, sem nenhum dos dois, espera o cancelamento.</summary>
    private sealed class StubGenerator(QuestionDraft? draft = null, QuestionDraftFailure? failure = null) : IQuestionDraftGenerator
    {
        public List<QuestionDraftRequest> Requests { get; } = [];

        public async Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (draft is null && failure is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return failure is { } reason
                ? throw new QuestionDraftGenerationException(reason)
                : new QuestionDraftBatch(draft is null ? [] : [draft], "gerar-questoes.v1", new AiUsage("Stub", "modelo-sintetico", 11, 22, TimeSpan.FromSeconds(2)));
        }
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new TestUser(null, null));

    private async Task<AppDbContext> MigratedAsync()
    {
        var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        return db;
    }

    private SuggestSingleDraft UseCase(
        AppDbContext db, IQuestionDraftGenerator generator, Guid? userId, string? role = RoleNames.Admin, string provider = AiOptions.Fake,
        int perAdmin = 60, int timeoutSeconds = 90) =>
        new(db, generator, _clock, new TestUser(userId, role),
            Options.Create(new AiOptions { Provider = provider, DailyQuotaPerAdmin = perAdmin, TimeoutSeconds = timeoutSeconds }));

    private static QuestionDraft Draft(string statement) => new(statement, FourOptions, 1, "Explicação sintética.");

    [Fact]
    public async Task Sugerir_devolve_o_rascunho_validado_grava_so_o_pedido_avulso_e_nao_cria_questao_nem_sugestao()
    {
        await using var db = await MigratedAsync();
        db.Questions.Add(new Question(QuizType.Esportivo, AgeBand.Age8To10, "Qual é a questão sintética já existente?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]));
        await db.SaveChangesAsync(Ct);
        var generator = new StubGenerator(Draft("  Qual é a sugestão avulsa sintética? "));

        var result = await UseCase(db, generator, Ana, RoleNames.Master).HandleAsync(QuizType.Esportivo, AgeBand.Age8To10, Ct);

        await using var read = NewContext();
        var job = await read.AiGenerationJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((SuggestSingleDraftStatus.Suggested, job.Id, "Qual é a sugestão avulsa sintética?"), (result.Status, result.JobId.GetValueOrDefault(), result.Draft?.Statement));
        Assert.Equal(FourOptions, result.Draft?.Options);
        Assert.Equal(
            (AiJobKind.Single, AiJobStatus.Succeeded, Ana, 1, null, "Stub", "modelo-sintetico", "gerar-questoes.v1", 11, 22),
            (job.Kind, job.Status, job.RequestedByUserId, job.RequestedCount, job.Subject, job.Provider, job.Model, job.PromptVersion, job.InputTokens, job.OutputTokens));
        Assert.Equal((1, 1, 0, 0), (job.GeneratedCount, job.ValidCount, job.InvalidCount, job.DuplicateCount));
        Assert.Equal((1, 0), (await read.Questions.CountAsync(Ct), await read.AiQuestionSuggestions.CountAsync(Ct))); // "Sugerir" nunca salva
        var sent = Assert.Single(generator.Requests); // ao gerador: tipo, faixa, uma questão, sem assunto, e os enunciados de Questions
        Assert.Equal((QuizType.Esportivo, AgeBand.Age8To10, 1, null), (sent.QuizType, sent.AgeBand, sent.Count, sent.Subject));
        Assert.Equal(["Qual é a questão sintética já existente?"], sent.ExistingStatements);
    }

    [Fact]
    public async Task Sem_IA_sem_perfil_ou_sem_saldo_o_gerador_nao_e_chamado_e_cada_sugestao_gasta_1_da_cota()
    {
        await using var db = await MigratedAsync();
        var generator = new FakeQuestionDraftGenerator(Options.Create(new FakeAiOptions()));
        var stub = new StubGenerator(Draft("Qual é a sugestão que não deve ser pedida?"));

        var off = await UseCase(db, stub, Ana, provider: AiOptions.Off).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        var student = await UseCase(db, stub, Ana, RoleNames.Student).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        await Assert.ThrowsAsync<DomainException>(() => UseCase(db, stub, Ana).HandleAsync((QuizType)99, AgeBand.Age6To7, Ct));
        var first = await UseCase(db, generator, Ana, perAdmin: 2).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        _clock.UtcNow = T0.AddMinutes(1);
        var second = await UseCase(db, generator, Ana, perAdmin: 2).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        var third = await UseCase(db, stub, Ana, perAdmin: 2).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);

        Assert.Equal(
            [SuggestSingleDraftStatus.Disabled, SuggestSingleDraftStatus.Forbidden, SuggestSingleDraftStatus.Suggested, SuggestSingleDraftStatus.Suggested, SuggestSingleDraftStatus.QuotaExceeded],
            new[] { off, student, first, second, third }.Select(r => r.Status));
        Assert.Equal(new AiQuotaExceeded(Global: false, Limit: 2, Remaining: 0, AvailableAt: T0.AddHours(24)), third.Quota);
        Assert.Empty(stub.Requests);
        Assert.Equal(2, await db.AiGenerationJobs.CountAsync(Ct)); // o recusado por cota não cria pedido
    }

    [Theory]
    [InlineData(QuestionDraftFailure.Refused, "ai_refused")]
    [InlineData(QuestionDraftFailure.ProviderUnavailable, "ai_unavailable")]
    [InlineData(null, "ai_timeout")] // o gerador não responde: vale o menor entre Ai:TimeoutSeconds (1 s aqui) e 20 s
    public async Task Falha_ou_prazo_estourado_devolvem_o_codigo_fechado_e_o_pedido_falho_conta_na_cota(QuestionDraftFailure? failure, string code)
    {
        await using var db = await MigratedAsync();

        var result = await UseCase(db, new StubGenerator(failure: failure), Ana, perAdmin: 1, timeoutSeconds: 1).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        var next = await UseCase(db, new StubGenerator(Draft("Qual é a próxima sugestão sintética?")), Ana, perAdmin: 1).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);

        await using var read = NewContext();
        var job = await read.AiGenerationJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((SuggestSingleDraftStatus.Failed, code, null, job.Id), (result.Status, result.FailureReason, result.Draft, result.JobId.GetValueOrDefault()));
        Assert.Equal((AiJobStatus.Failed, code), (job.Status, job.FailureReason));
        Assert.Equal(SuggestSingleDraftStatus.QuotaExceeded, next.Status);
        Assert.Equal(TimeSpan.FromSeconds(20), SuggestSingleDraft.MaxWait);
    }

    [Theory]
    [InlineData("Qual é o <b>rascunho</b> sintético com marcação?", 1, 0)]
    [InlineData("QUAL E A QUESTAO SINTETICA JA EXISTENTE", 0, 1)]
    public async Task Sugestao_invalida_ou_duplicada_nao_e_devolvida(string statement, int invalid, int duplicate)
    {
        await using var db = await MigratedAsync();
        db.Questions.Add(new Question(QuizType.Cultural, AgeBand.Age6To7, "Qual é a questão sintética já existente?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]));
        await db.SaveChangesAsync(Ct);

        var result = await UseCase(db, new StubGenerator(Draft(statement)), Ana).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);

        var job = await db.AiGenerationJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((SuggestSingleDraftStatus.Rejected, null), (result.Status, result.Draft));
        Assert.Equal((AiJobStatus.Succeeded, 1, 0, invalid, duplicate), (job.Status, job.GeneratedCount, job.ValidCount, job.InvalidCount, job.DuplicateCount));
    }

    [Fact]
    public async Task Ao_salvar_so_o_pedido_avulso_do_mesmo_usuario_concluido_e_recente_da_origem_IA_a_questao()
    {
        await using var db = await MigratedAsync();
        var suggested = await UseCase(db, new StubGenerator(Draft("Qual é a sugestão avulsa sintética?")), Ana).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        var failed = await UseCase(db, new StubGenerator(failure: QuestionDraftFailure.Refused), Ana).HandleAsync(QuizType.Cultural, AgeBand.Age6To7, Ct);
        var batch = new AiGenerationJob(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age6To7, 1, null, Ana, T0);
        batch.Succeed(new AiJobUsage("Fake", "fake-v1", "gerar-questoes.v1", 1, 1, TimeSpan.Zero), new AiJobCounts(1, 1, 0, 0), T0);
        db.AiGenerationJobs.Add(batch);
        await db.SaveChangesAsync(Ct);
        _clock.UtcNow = T0.AddHours(2).AddSeconds(-1);
        var number = 0;
        Task<(Guid QuestionId, QuestionOrigin Origin)> SaveAsync(Guid? jobId, Guid? userId = null, string? role = RoleNames.Admin) =>
            UseCase(db, new StubGenerator(), userId ?? Ana, role, provider: AiOptions.Off) // a origem não depende de a IA estar ligada agora
                .SaveAsync(jobId, QuizType.Cultural, AgeBand.Age6To7, $"Qual é a questão sintética salva {++number}?", "Explicação.", FourOptions, 2, Ct);

        var fromAi = await SaveAsync(suggested.JobId);
        QuestionOrigin[] team =
        [
            (await SaveAsync(null)).Origin, (await SaveAsync(Guid.NewGuid())).Origin, (await SaveAsync(failed.JobId)).Origin,
            (await SaveAsync(batch.Id)).Origin, (await SaveAsync(suggested.JobId, Bia)).Origin, (await SaveAsync(suggested.JobId, role: RoleNames.Student)).Origin,
        ];
        _clock.UtcNow = T0.AddHours(2).AddSeconds(1); // o pedido deixou de ser recente
        var late = await SaveAsync(suggested.JobId);
        await Assert.ThrowsAsync<DomainException>(
            () => UseCase(db, new StubGenerator(), Ana).SaveAsync(suggested.JobId, QuizType.Cultural, AgeBand.Age6To7, " ", "Explicação.", FourOptions, 2, Ct));

        var question = await db.Questions.AsNoTracking().SingleAsync(q => q.Id == fromAi.QuestionId, Ct);
        Assert.Equal(
            (QuestionOrigin.Ai, QuestionOrigin.Ai, suggested.JobId, "gerar-questoes.v1", T0, QuestionStatus.Draft),
            (fromAi.Origin, question.Origin, question.AiJobId, question.PromptVersion, question.GeneratedAt.GetValueOrDefault(), question.Status));
        Assert.All([.. team, late.Origin], origin => Assert.Equal(QuestionOrigin.Team, origin));
        Assert.Equal(8, await db.Questions.CountAsync(Ct));
        Assert.Equal(7, await db.Questions.CountAsync(q => q.AiJobId == null && q.PromptVersion == null && q.GeneratedAt == null, Ct));
    }
}
