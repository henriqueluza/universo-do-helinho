using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #257: a migração única da IA (ADR-0004 §9.2). Sem host web: só o contexto e o banco.</summary>
[Collection(PostgresCollection.Name)]
public class AiSchemaTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _database = $"ia_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new AnonymousCurrentUser());

    private static Task<List<string>> ColumnsAsync(AppDbContext db, string table) =>
        db.Database.SqlQuery<string>($"""
            SELECT column_name || ' ' || CASE WHEN data_type = 'character varying' THEN 'varchar(' || character_maximum_length || ')' ELSE data_type END
                   || CASE is_nullable WHEN 'NO' THEN ' not null' ELSE '' END AS "Value"
              FROM information_schema.columns WHERE table_name = {table} ORDER BY ordinal_position
            """).ToListAsync(Ct);

    [Fact]
    public async Task Migracao_da_IA_vem_depois_da_fila_e_do_email_do_responsavel_e_cria_as_tabelas_do_ADR()
    {
        await using var db = NewContext();
        var migrations = db.Database.GetMigrations().Select(id => id[(id.IndexOf('_', StringComparison.Ordinal) + 1)..]).ToList();
        await db.Database.MigrateAsync(Ct);

        Assert.Equal(["AddQueueJobs", "AddStudentGuardianEmail", "AddAiQuestionDrafts"], migrations.TakeLast(3));
        const string ts = "timestamp with time zone";
        Assert.Equal(
            ["Id uuid not null", "Kind varchar(20) not null", "QuizType varchar(20) not null", "AgeBand varchar(20) not null",
                "RequestedCount integer not null", "Subject varchar(120)", "RequestedByUserId uuid not null", $"CreatedAt {ts} not null",
                "Status varchar(20) not null", $"StartedAt {ts}", $"FinishedAt {ts}", "FailureReason varchar(30)", "Provider varchar(40)",
                "Model varchar(100)", "PromptVersion varchar(60)", "InputTokens integer", "OutputTokens integer", "DurationMs integer",
                "GeneratedCount integer not null", "ValidCount integer not null", "InvalidCount integer not null", "DuplicateCount integer not null"],
            await ColumnsAsync(db, "AiGenerationJobs")); // só o id de quem pediu: nenhuma coluna de nome, e-mail ou unidade
        Assert.Equal(
            ["Id uuid not null", "JobId uuid not null", "Position integer not null", "Statement varchar(500) not null",
                "OptionA varchar(300) not null", "OptionB varchar(300) not null", "OptionC varchar(300) not null", "OptionD varchar(300) not null",
                "CorrectIndex integer not null", "Explanation varchar(1000) not null", "Status varchar(20) not null", "DecidedByUserId uuid",
                $"DecidedAt {ts}", "QuestionId uuid", $"ContentPurgedAt {ts}"],
            await ColumnsAsync(db, "AiQuestionSuggestions"));
        Assert.Equal(
            [$"AiEditedAt {ts}", "AiJobId uuid", $"GeneratedAt {ts}", "Origin varchar(20) not null", "PromptVersion varchar(60)"],
            (await ColumnsAsync(db, "Questions")).TakeLast(5));

        var indexes = await db.Database.SqlQuery<string>(
            $"""SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename IN ('AiGenerationJobs', 'AiQuestionSuggestions')""").ToListAsync(Ct);
        Assert.Contains(indexes, i => i.EndsWith("\"AiGenerationJobs\" USING btree (\"RequestedByUserId\", \"CreatedAt\")", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.EndsWith("\"AiGenerationJobs\" USING btree (\"CreatedAt\")", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal) && i.EndsWith("(\"JobId\", \"Position\")", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.EndsWith("\"AiQuestionSuggestions\" USING btree (\"Status\")", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Questao_que_ja_existia_recebe_origem_Team()
    {
        await using var db = NewContext();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("AddStudentGuardianEmail", Ct);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Questions" ("Id", "QuizType", "AgeBand", "Statement", "Explanation", "Status")
            VALUES ({id}, 'Cultural', 'Age8To10', 'Enunciado sintético anterior à IA?', 'Explicação sintética.', 'Approved')
            """, Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var question = await db.Questions.AsNoTracking().SingleAsync(q => q.Id == id, Ct);
        Assert.Equal((QuestionOrigin.Team, QuestionStatus.Approved), (question.Origin, question.Status));
        Assert.Equal((null, null, null, null), (question.AiJobId, question.PromptVersion, question.GeneratedAt, question.AiEditedAt));
    }

    [Fact]
    public async Task Pedido_sugestao_e_questao_de_IA_sao_gravados_com_a_proveniencia()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var job = new AiGenerationJob(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age8To10, 2, "Festas juninas", User, Now);
        var suggestion = new AiQuestionSuggestion(job.Id, 0, "Enunciado sintético da IA?", ["Um", "Dois", "Três", "Quatro"], 1, "Explicação sintética.");
        var question = Question.FromAi(job.QuizType, job.AgeBand, suggestion.Statement, suggestion.Explanation,
            [.. suggestion.Options.Select((o, i) => (o, i == suggestion.CorrectIndex))], job.Id, "gerar-questoes.v1", Now);
        db.AddRange(job, suggestion, question);
        await db.SaveChangesAsync(Ct);
        await using var read = NewContext();
        var saved = await read.AiQuestionSuggestions.SingleAsync(Ct);
        var created = await read.Questions.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((job.Id, AiSuggestionStatus.Pending, 1), (saved.JobId, saved.Status, saved.CorrectIndex));
        Assert.Equal((QuestionOrigin.Ai, job.Id, "gerar-questoes.v1", Now), (created.Origin, created.AiJobId, created.PromptVersion, created.GeneratedAt));
        // §9.3: a decisão sobre a sugestão leva o Status lido; a segunda gravação simultânea é recusada pelo banco.
        await db.AiQuestionSuggestions.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AiSuggestionStatus.Discarded), Ct);
        read.Entry(saved).Property(s => s.Status).CurrentValue = AiSuggestionStatus.Approved;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => read.SaveChangesAsync(Ct));
        db.Add(new AiQuestionSuggestion(job.Id, 0, "Outro enunciado sintético?", ["Um", "Dois", "Três", "Quatro"], 0, "Explicação sintética.")); // índice único (JobId, Position)
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct))).InnerException as PostgresException)?.SqlState);
    }
}
