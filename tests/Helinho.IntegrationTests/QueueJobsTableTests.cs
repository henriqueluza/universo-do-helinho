using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Tabela da fila (ADR-0003 §5.1) e guarda de posse (§6.6), sem a porta: só a entidade, o contexto e o banco.</summary>
[Collection(PostgresCollection.Name)]
public class QueueJobsTableTests(PostgresFixture postgres)
{
    private const string Key = "email.admin-invite:22222222-2222-2222-2222-222222222222:14712345";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string _database = $"fila_tabela_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new AnonymousCurrentUser());

    private async Task<AppDbContext> MigratedAsync()
    {
        var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        return db;
    }

    private static QueueJob Job(string? key = null) => new(new JobRequest(
        "email.admin-invite", Guid.Parse("22222222-2222-2222-2222-222222222222"), new JobPayload().WithCode("reason", "resend"),
        TimeSpan.FromHours(24), key), Now);

    private static async Task<QueueJob> SavedAsync(AppDbContext db, QueueJob job)
    {
        db.QueueJobs.Add(job);
        await db.SaveChangesAsync(Ct);
        return job;
    }

    [Fact]
    public async Task Tabela_tem_as_colunas_os_tipos_e_os_indices_do_ADR()
    {
        await using var db = await MigratedAsync();

        var columns = await db.Database.SqlQuery<string>($"""
            SELECT column_name || ' ' || CASE WHEN data_type = 'character varying' THEN 'varchar(' || character_maximum_length || ')' ELSE data_type END
                   || CASE is_nullable WHEN 'NO' THEN ' not null' ELSE '' END AS "Value"
              FROM information_schema.columns WHERE table_name = 'QueueJobs' ORDER BY ordinal_position
            """).ToListAsync(Ct);
        var indexes = await db.Database.SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename = 'QueueJobs'""").ToListAsync(Ct);

        Assert.Equal(
            ["Id uuid not null", "Type varchar(60) not null", "SubjectId uuid", "Payload jsonb not null", "Status varchar(20) not null",
                "Attempts integer not null", "NextAttemptAt timestamp with time zone not null", "ExpiresAt timestamp with time zone not null",
                "LastError varchar(100)", "IdempotencyKey varchar(200)", "ExternalId varchar(200)", "Feedback varchar(30)",
                "FeedbackAt timestamp with time zone", "CreatedAt timestamp with time zone not null", "FinishedAt timestamp with time zone"],
            columns); // nenhuma coluna de destinatário, corpo, link ou token
        Assert.Equal(["Pending", "Processing", "Succeeded", "Failed", "Cancelled"], Enum.GetNames<QueueJobStatus>());
        Assert.Equal(5, indexes.Count); // a chave primária e os quatro do ADR
        Assert.Contains(indexes, i => i.Contains("\"IX_QueueJobs_Type_NextAttemptAt\"") && i.Contains("(\"Type\", \"NextAttemptAt\")")
            && i.Contains("'Pending'") && i.Contains("'Processing'"));
        Assert.Contains(indexes, i => i.StartsWith("CREATE UNIQUE INDEX \"UX_QueueJobs_IdempotencyKey\"", StringComparison.Ordinal)
            && i.EndsWith("WHERE (\"IdempotencyKey\" IS NOT NULL)", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.Contains("\"IX_QueueJobs_Type_SubjectId_CreatedAt\"") && i.EndsWith("(\"Type\", \"SubjectId\", \"CreatedAt\")", StringComparison.Ordinal));
        Assert.Contains(indexes, i => i.Contains("\"IX_QueueJobs_ExternalId\"") && i.EndsWith("WHERE (\"ExternalId\" IS NOT NULL)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trabalho_nasce_pendente_sem_tentativas_e_o_Status_e_gravado_como_texto()
    {
        await using var db = await MigratedAsync();
        await SavedAsync(db, Job(Key));

        var row = JsonDocument.Parse(await db.Database.SqlQuery<string>($"""SELECT row_to_json(q)::text AS "Value" FROM "QueueJobs" q""").SingleAsync(Ct)).RootElement;

        Assert.Equal("Pending", row.GetProperty("Status").GetString());
        Assert.Equal(0, row.GetProperty("Attempts").GetInt32());
        Assert.Equal(Now, row.GetProperty("CreatedAt").GetDateTimeOffset());
        Assert.Equal(Now, row.GetProperty("NextAttemptAt").GetDateTimeOffset());
        Assert.Equal(Now.AddHours(24), row.GetProperty("ExpiresAt").GetDateTimeOffset());
        Assert.Equal(Key, row.GetProperty("IdempotencyKey").GetString());
    }

    [Fact]
    public async Task Indice_unico_recusa_a_mesma_chave_e_aceita_varios_sem_chave()
    {
        await using var db = await MigratedAsync();
        await SavedAsync(db, Job(Key));

        await using var second = NewContext();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SavedAsync(second, Job(Key)));
        Assert.Equal("UX_QueueJobs_IdempotencyKey", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);

        await using var third = NewContext();
        third.QueueJobs.AddRange(Job(), Job()); // índice parcial: sem chave não há idempotência
        await third.SaveChangesAsync(Ct);
        Assert.Equal(3, await third.QueueJobs.CountAsync(Ct));
    }

    [Fact]
    public async Task Cancelar_so_vale_para_pendente_e_libera_a_chave_no_banco()
    {
        await using var db = await MigratedAsync();
        var cancelled = await SavedAsync(db, Job(Key));
        var processing = await SavedAsync(db, Job());
        await db.Database.ExecuteSqlAsync($"""UPDATE "QueueJobs" SET "Status" = 'Processing', "Attempts" = 1 WHERE "Id" = {processing.Id}""", Ct);

        cancelled.Cancel(Now.AddMinutes(1));
        db.QueueJobs.Add(Job(Key)); // reenvio na mesma janela, na mesma gravação: a chave já foi liberada
        await db.SaveChangesAsync(Ct);

        Assert.Throws<InvalidOperationException>(() => cancelled.Cancel(Now)); // já cancelado
        await using var check = NewContext();
        var taken = await check.QueueJobs.SingleAsync(j => j.Id == processing.Id, Ct);
        Assert.Throws<InvalidOperationException>(() => taken.Cancel(Now)); // o trabalhador já pegou
        var stored = await check.QueueJobs.SingleAsync(j => j.Id == cancelled.Id, Ct);
        Assert.Equal((QueueJobStatus.Cancelled, Now.AddMinutes(1), null), (stored.Status, stored.FinishedAt, stored.IdempotencyKey));
        Assert.Equal(1, await check.QueueJobs.CountAsync(j => j.IdempotencyKey == Key && j.Status == QueueJobStatus.Pending, Ct));
    }

    [Fact]
    public async Task Cancelamento_e_recusado_se_o_trabalhador_contou_uma_tentativa_antes()
    {
        await using var db = await MigratedAsync();
        var job = await SavedAsync(db, Job());
        // O trabalhador pegou (Processing, 1) e devolveu para nova tentativa (Pending, 1): só Attempts denuncia a troca.
        await db.Database.ExecuteSqlAsync($"""UPDATE "QueueJobs" SET "Attempts" = 1 WHERE "Id" = {job.Id}""", Ct);

        job.Cancel(Now);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Gravacao_do_trabalhador_e_recusada_se_o_cancelamento_salvou_antes()
    {
        await using var db = await MigratedAsync();
        var job = await SavedAsync(db, Job());
        await using var worker = NewContext();
        var read = await worker.QueueJobs.SingleAsync(j => j.Id == job.Id, Ct); // leu o pendente antes do cancelamento
        job.Cancel(Now);
        await db.SaveChangesAsync(Ct); // Attempts continua 0: só Status denuncia a troca

        worker.Entry(read).Property(j => j.Status).CurrentValue = QueueJobStatus.Processing;
        worker.Entry(read).Property(j => j.Attempts).CurrentValue = 1;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.SaveChangesAsync(Ct));
        Assert.Equal(QueueJobStatus.Cancelled, await db.QueueJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).SingleAsync(Ct));
    }
}
