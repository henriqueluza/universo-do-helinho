using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #287: <c>SaveQuestion</c> passa a hora da edição e a questão de origem IA grava <c>AiEditedAt</c> (ADR-0004 §9.4, D5).</summary>
[Collection(PostgresCollection.Name)]
public class AiEditedAtTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EditedAt = new(2026, 11, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly string[] Options = ["Um", "Dois", "Três", "Quatro"];
    private readonly string _database = $"ia_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record NoUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? UnitId => null;
        public bool IsInRole(string role) => false;
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new NoUser());

    private async Task<(AppDbContext Db, Guid Id)> SeedAsync(bool fromAi)
    {
        var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var job = new AiGenerationJob(AiJobKind.Single, QuizType.Cultural, AgeBand.Age8To10, 1, null, Guid.NewGuid(), GeneratedAt);
        db.AiGenerationJobs.Add(job);
        List<(string, bool)> items = [("Um", true), ("Dois", false), ("Três", false), ("Quatro", false)];
        var question = fromAi
            ? Question.FromAi(QuizType.Cultural, AgeBand.Age8To10, "Qual é a questão sintética?", "Explicação.", items, job.Id, "gerar-questoes.v1", GeneratedAt)
            : new Question(QuizType.Cultural, AgeBand.Age8To10, "Qual é a questão sintética?", "Explicação.", items);
        question.Approve();
        db.Questions.Add(question);
        await db.SaveChangesAsync(Ct);
        return (db, question.Id);
    }

    private static Task<Guid?> SaveAsync(AppDbContext db, Guid id, DateTimeOffset? editedAt) =>
        SaveQuestion.HandleAsync(db, id, QuizType.Cultural, AgeBand.Age8To10, "Qual é a questão editada?", "Explicação nova.", Options, 1, Ct, editedAt);

    [Fact]
    public async Task Editar_questao_de_origem_IA_grava_a_hora_mantem_a_origem_e_volta_a_rascunho()
    {
        var (db, id) = await SeedAsync(fromAi: true);
        await using var _ = db;

        Assert.Equal(id, await SaveAsync(db, id, EditedAt));

        await using var read = NewContext();
        var question = await read.Questions.AsNoTracking().SingleAsync(q => q.Id == id, Ct);
        Assert.Equal((QuestionOrigin.Ai, EditedAt, QuestionStatus.Draft), (question.Origin, question.AiEditedAt, question.Status));
    }

    [Fact]
    public async Task Editar_questao_da_equipe_nao_grava_AiEditedAt_e_criar_questao_tambem_nao()
    {
        var (db, id) = await SeedAsync(fromAi: false);
        await using var _ = db;

        await SaveAsync(db, id, EditedAt);
        var created = await SaveQuestion.HandleAsync(db, null, QuizType.Cultural, AgeBand.Age6To7, "Qual é a questão criada?", "Explicação.", Options, 0, Ct, EditedAt);

        await using var read = NewContext();
        var all = await read.Questions.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(2, all.Count);
        Assert.All(all, q => Assert.Null(q.AiEditedAt));
        Assert.Equal(QuestionOrigin.Team, all.Single(q => q.Id == created).Origin);
    }

    [Fact]
    public async Task Sem_a_hora_a_edicao_de_questao_IA_nao_registra_AiEditedAt()
    {
        var (db, id) = await SeedAsync(fromAi: true);
        await using var _ = db;

        await SaveAsync(db, id, editedAt: null);

        await using var read = NewContext();
        Assert.Null((await read.Questions.AsNoTracking().SingleAsync(q => q.Id == id, Ct)).AiEditedAt);
    }
}
