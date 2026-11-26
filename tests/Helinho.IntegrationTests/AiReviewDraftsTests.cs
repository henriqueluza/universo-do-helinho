using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #260: revisão humana das sugestões da IA (ADR-0004 §9.3 e §8). Sem host web.</summary>
[Collection(PostgresCollection.Name)]
public class AiReviewDraftsTests(PostgresFixture postgres)
{
    private const string Statement = "Qual é a sugestão sintética da festa junina?";
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Generated = T0.AddSeconds(9);
    private static readonly DateTimeOffset ReviewedAt = T0.AddMinutes(5);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bia = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string[] FourOptions = ["Um", "Dois", "Três", "Quatro"];
    private readonly string _database = $"ia_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => ReviewedAt;
    }

    private sealed record TestUser(Guid? UserId, string? Role) : ICurrentUser
    {
        public bool IsAuthenticated => UserId is not null;
        public Guid? UnitId => null;
        public bool IsInRole(string role) => role == Role;
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new TestUser(null, null));

    private static ReviewAiDrafts UseCase(AppDbContext db, Guid? userId, string? role = RoleNames.Admin, string provider = AiOptions.Fake) =>
        new(db, new FixedClock(), new TestUser(userId, role), Options.Create(new AiOptions { Provider = provider }));

    /// <summary>Um pedido concluído com uma sugestão pendente, e nenhuma questão.</summary>
    private async Task<(AiGenerationJob Job, AiQuestionSuggestion Suggestion)> SeedAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var job = new AiGenerationJob(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age8To10, 1, null, Ana, T0);
        job.Succeed(new AiJobUsage("Fake", "fake-v1", "gerar-questoes.v1", 10, 20, TimeSpan.FromSeconds(1)), new AiJobCounts(1, 1, 0, 0), Generated);
        var suggestion = new AiQuestionSuggestion(job.Id, 0, Statement, FourOptions, 2, "Explicação sintética.");
        db.AddRange(job, suggestion);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(0, await db.Questions.CountAsync(Ct)); // antes de uma pessoa decidir, a sugestão não está em Questions
        return (job, suggestion);
    }

    private async Task<(AiQuestionSuggestion Suggestion, List<Question> Questions)> ReadAsync()
    {
        await using var db = NewContext();
        return (await db.AiQuestionSuggestions.AsNoTracking().SingleAsync(Ct), await db.Questions.AsNoTracking().Include(q => q.Options).ToListAsync(Ct));
    }

    [Fact]
    public async Task Aprovar_cria_a_questao_aprovada_com_a_proveniencia_e_marca_a_sugestao()
    {
        var (job, seeded) = await SeedAsync();
        await using var db = NewContext();

        var decision = await UseCase(db, Bia, RoleNames.Master).ApproveAsync(seeded.Id, Ct);

        var (suggestion, questions) = await ReadAsync();
        var question = Assert.Single(questions);
        Assert.Equal((AiDraftDecisionStatus.Done, question.Id), (decision.Status, decision.QuestionId.GetValueOrDefault()));
        Assert.Equal(
            (QuestionStatus.Approved, QuestionOrigin.Ai, job.Id, "gerar-questoes.v1", Generated, QuizType.Cultural, AgeBand.Age8To10, Statement),
            (question.Status, question.Origin, question.AiJobId.GetValueOrDefault(), question.PromptVersion, question.GeneratedAt.GetValueOrDefault(),
                question.QuizType, question.AgeBand, question.Statement));
        Assert.Equal(FourOptions, question.Options.OrderBy(o => o.Key).Select(o => o.Label));
        Assert.Equal("c", question.Options.Single(o => o.IsCorrect).Key);
        Assert.Equal((AiSuggestionStatus.Approved, Bia, ReviewedAt, question.Id),
            (suggestion.Status, suggestion.DecidedByUserId.GetValueOrDefault(), suggestion.DecidedAt.GetValueOrDefault(), suggestion.QuestionId.GetValueOrDefault()));
    }

    [Fact]
    public async Task Aprovacao_e_barrada_quando_ja_existe_questao_igual_do_mesmo_tipo_e_faixa()
    {
        var (_, seeded) = await SeedAsync();
        await using var db = NewContext();
        db.Questions.AddRange( // criadas entre a geração e a revisão; a de outra faixa não conta
            new Question(QuizType.Cultural, AgeBand.Age8To10, "QUAL E A SUGESTAO SINTETICA DA FESTA JUNINA", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]),
            new Question(QuizType.Cultural, AgeBand.Age13To14, Statement, "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]));
        await db.SaveChangesAsync(Ct);

        var decision = await UseCase(db, Ana).ApproveAsync(seeded.Id, Ct);

        var (suggestion, questions) = await ReadAsync();
        Assert.Equal((AiDraftDecisionStatus.Duplicate, null), (decision.Status, decision.QuestionId));
        Assert.Equal((AiSuggestionStatus.Pending, 2), (suggestion.Status, questions.Count)); // segue pendente: a pessoa edita ou descarta
        Assert.All(questions, q => Assert.Equal(QuestionOrigin.Team, q.Origin));
    }

    [Fact]
    public async Task Salvar_a_edicao_cria_a_questao_em_rascunho_com_origem_IA_e_texto_invalido_deixa_a_sugestao_pendente()
    {
        var (job, seeded) = await SeedAsync();
        await using var db = NewContext();
        var useCase = UseCase(db, Ana);

        await Assert.ThrowsAsync<DomainException>(() => useCase.SaveEditedAsync(seeded.Id, QuizType.Cultural, AgeBand.Age8To10, " ", "Explicação.", FourOptions, 0, Ct));
        await Assert.ThrowsAsync<DomainException>(
            () => useCase.SaveEditedAsync(seeded.Id, QuizType.Cultural, AgeBand.Age8To10, new string('a', 501), "Explicação.", FourOptions, 0, Ct));
        var untouched = await ReadAsync();
        Assert.Equal((AiSuggestionStatus.Pending, 0), (untouched.Suggestion.Status, untouched.Questions.Count));
        var decision = await useCase.SaveEditedAsync(
            seeded.Id, QuizType.Cultural, AgeBand.Age11To12, "Qual é o enunciado sintético reescrito?", "Explicação revista.", ["Um", "Dois", "Três", "Cinco"], 3, Ct);

        var (suggestion, questions) = await ReadAsync();
        var question = Assert.Single(questions);
        Assert.Equal(AiDraftDecisionStatus.Done, decision.Status);
        Assert.Equal(
            (QuestionStatus.Draft, QuestionOrigin.Ai, job.Id, "gerar-questoes.v1", Generated, AgeBand.Age11To12, "Qual é o enunciado sintético reescrito?"),
            (question.Status, question.Origin, question.AiJobId.GetValueOrDefault(), question.PromptVersion, question.GeneratedAt.GetValueOrDefault(),
                question.AgeBand, question.Statement));
        Assert.Equal("Cinco", question.Options.Single(o => o.IsCorrect).Label);
        Assert.Equal((AiSuggestionStatus.Edited, Ana, question.Id), (suggestion.Status, suggestion.DecidedByUserId.GetValueOrDefault(), suggestion.QuestionId.GetValueOrDefault()));
    }

    [Fact]
    public async Task Descartar_marca_quem_e_quando_sem_criar_questao_e_a_segunda_decisao_recebe_ja_revisada()
    {
        var (_, seeded) = await SeedAsync();
        await using var db = NewContext();

        var discarded = await UseCase(db, Ana).DiscardAsync(seeded.Id, Ct);
        await using var later = NewContext();
        var approveAfter = await UseCase(later, Bia).ApproveAsync(seeded.Id, Ct);
        var editAfter = await UseCase(later, Bia).SaveEditedAsync(seeded.Id, QuizType.Cultural, AgeBand.Age8To10, Statement, "Explicação.", FourOptions, 0, Ct);

        var (suggestion, questions) = await ReadAsync();
        Assert.Equal((AiDraftDecisionStatus.Done, null), (discarded.Status, discarded.QuestionId));
        Assert.Equal((AiDraftDecisionStatus.AlreadyReviewed, AiDraftDecisionStatus.AlreadyReviewed), (approveAfter.Status, editAfter.Status));
        Assert.Equal((AiSuggestionStatus.Discarded, Ana, ReviewedAt, null),
            (suggestion.Status, suggestion.DecidedByUserId.GetValueOrDefault(), suggestion.DecidedAt.GetValueOrDefault(), suggestion.QuestionId));
        Assert.Empty(questions);
    }

    [Fact]
    public async Task Duas_pessoas_ao_mesmo_tempo_so_a_primeira_decisao_vale_e_a_questao_da_segunda_nao_entra()
    {
        var (_, seeded) = await SeedAsync();
        await using var slow = NewContext();
        await slow.AiQuestionSuggestions.SingleAsync(Ct); // a segunda pessoa já leu a sugestão como pendente
        await using var fast = NewContext();

        var first = await UseCase(fast, Ana).DiscardAsync(seeded.Id, Ct);
        var second = await UseCase(slow, Bia).ApproveAsync(seeded.Id, Ct);

        var (suggestion, questions) = await ReadAsync();
        Assert.Equal((AiDraftDecisionStatus.Done, AiDraftDecisionStatus.AlreadyReviewed), (first.Status, second.Status));
        Assert.Equal((AiSuggestionStatus.Discarded, Ana, 0), (suggestion.Status, suggestion.DecidedByUserId.GetValueOrDefault(), questions.Count));
    }

    [Fact]
    public async Task Com_a_IA_desligada_sem_perfil_ou_sem_a_sugestao_nada_muda()
    {
        var (_, seeded) = await SeedAsync();
        await using var db = NewContext();

        AiDraftDecision[] decisions =
        [
            await UseCase(db, Ana, provider: AiOptions.Off).ApproveAsync(seeded.Id, Ct),
            await UseCase(db, Ana, RoleNames.Student).ApproveAsync(seeded.Id, Ct),
            await UseCase(db, null).DiscardAsync(seeded.Id, Ct),
            await UseCase(db, Ana).DiscardAsync(Guid.NewGuid(), Ct),
        ];

        var (suggestion, questions) = await ReadAsync();
        Assert.Equal(
            [AiDraftDecisionStatus.Disabled, AiDraftDecisionStatus.Forbidden, AiDraftDecisionStatus.Forbidden, AiDraftDecisionStatus.NotFound],
            decisions.Select(d => d.Status));
        Assert.Equal((AiSuggestionStatus.Pending, 0), (suggestion.Status, questions.Count));
    }
}
