using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Fila persistente (ADR-0003 §5): tabela, idempotência e cancelamento. Sem host web: só o contexto e o banco.</summary>
[Collection(PostgresCollection.Name)]
public class JobQueueTests(PostgresFixture postgres)
{
    private const string Invite = "email.admin-invite";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Subject = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string Key = $"{Invite}:{Subject}:14712345";
    private static readonly FixedClock Clock = new();
    private readonly string _database = $"fila_{Guid.NewGuid():N}"; // um banco por teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

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

    private static JobRequest Request(string? key = null, Guid? subject = null, string type = Invite) =>
        new(type, subject ?? Subject, new JobPayload().WithCode("reason", "resend"), TimeSpan.FromHours(24), key);

    [Fact]
    public async Task Enfileirar_nao_salva_e_o_trabalho_nasce_pendente_so_com_referencias()
    {
        await using var db = await MigratedAsync();

        var id = await new JobQueue(db, Clock).EnqueueAsync(Request(Key), Ct);
        Assert.Equal(0, await db.QueueJobs.CountAsync(Ct)); // §5.5: quem chama é quem salva
        await db.SaveChangesAsync(Ct);

        var json = await db.Database.SqlQuery<string>($"""SELECT row_to_json(q)::text AS "Value" FROM "QueueJobs" q""").SingleAsync(Ct);
        var row = JsonDocument.Parse(json).RootElement;
        Assert.Equal(
            ["Id", "Type", "SubjectId", "Payload", "Status", "Attempts", "NextAttemptAt", "ExpiresAt", "LastError", "IdempotencyKey",
                "ExternalId", "Feedback", "FeedbackAt", "CreatedAt", "FinishedAt"],
            row.EnumerateObject().Select(p => p.Name)); // nenhuma coluna de destinatário, corpo, link ou token
        Assert.Equal(id, row.GetProperty("Id").GetGuid());
        Assert.Equal("Pending", row.GetProperty("Status").GetString());
        Assert.Equal(0, row.GetProperty("Attempts").GetInt32());
        Assert.Equal(Now, row.GetProperty("NextAttemptAt").GetDateTimeOffset());
        Assert.Equal(Now, row.GetProperty("CreatedAt").GetDateTimeOffset());
        Assert.Equal(Now.AddHours(24), row.GetProperty("ExpiresAt").GetDateTimeOffset());
        Assert.Equal(["v", "reason"], row.GetProperty("Payload").EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, row.GetProperty("Payload").GetProperty("v").GetInt32());
        Assert.Equal("resend", JobCode.Require(row.GetProperty("Payload").GetProperty("reason").GetString() ?? "", "reason")); // código, não texto livre
        Assert.DoesNotContain("@", json); // nenhum endereço em repouso
    }

    [Fact]
    public async Task Tabela_tem_os_tipos_os_estados_e_os_indices_do_ADR()
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
            columns);
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
    public async Task Mesma_chave_de_idempotencia_nao_enfileira_duas_vezes()
    {
        await using var db = await MigratedAsync();
        var queue = new JobQueue(db, Clock);

        Assert.NotNull(await queue.EnqueueAsync(Request(Key), Ct));
        Assert.Null(await queue.EnqueueAsync(Request(Key), Ct)); // clique duplo na mesma unidade de trabalho
        await db.SaveChangesAsync(Ct);
        await using var later = NewContext();
        Assert.Null(await new JobQueue(later, Clock).EnqueueAsync(Request(Key), Ct)); // reenvio do formulário pelo navegador

        // Sem chave não há idempotência: o índice único é parcial e os dois entram.
        Assert.NotNull(await queue.EnqueueAsync(Request(), Ct));
        Assert.NotNull(await queue.EnqueueAsync(Request(), Ct));
        await db.SaveChangesAsync(Ct);
        Assert.Equal(3, await later.QueueJobs.CountAsync(Ct));
    }

    [Fact]
    public async Task Indice_unico_recusa_a_segunda_gravacao_quando_dois_pedidos_correm_juntos()
    {
        await using var first = await MigratedAsync();
        await using var second = NewContext();
        Assert.NotNull(await new JobQueue(first, Clock).EnqueueAsync(Request(Key), Ct));
        Assert.NotNull(await new JobQueue(second, Clock).EnqueueAsync(Request(Key), Ct)); // nenhum dos dois salvou ainda

        await first.SaveChangesAsync(Ct);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));

        Assert.Equal("UX_QueueJobs_IdempotencyKey", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
    }

    [Fact]
    public async Task Cancelar_pendentes_atinge_so_os_pendentes_do_tipo_e_do_sujeito_e_libera_a_chave()
    {
        await using var db = await MigratedAsync();
        var queue = new JobQueue(db, Clock);
        var target = (await queue.EnqueueAsync(Request(Key), Ct)).GetValueOrDefault();
        var processing = (await queue.EnqueueAsync(Request(), Ct)).GetValueOrDefault();
        var otherSubject = (await queue.EnqueueAsync(Request(subject: Guid.NewGuid()), Ct)).GetValueOrDefault();
        var otherType = (await queue.EnqueueAsync(Request(type: "email.password-reset"), Ct)).GetValueOrDefault();
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlAsync($"""UPDATE "QueueJobs" SET "Status" = 'Processing', "Attempts" = 1 WHERE "Id" = {processing}""", Ct);

        await using var useCase = NewContext();
        var resend = new JobQueue(useCase, Clock);
        Assert.Equal(1, await resend.CancelPendingAsync(Invite, Subject, Ct));
        Assert.Equal(0, await resend.CancelPendingAsync(Invite, Subject, Ct)); // já cancelado neste contexto
        var resent = (await resend.EnqueueAsync(Request(Key), Ct)).GetValueOrDefault(); // reenvio na mesma janela de idempotência
        Assert.Equal(0, await db.QueueJobs.CountAsync(j => j.Status == QueueJobStatus.Cancelled, Ct)); // cancelar também não salva
        await useCase.SaveChangesAsync(Ct);

        await using var check = NewContext();
        var jobs = await check.QueueJobs.ToDictionaryAsync(j => j.Id, Ct);
        Assert.Equal(QueueJobStatus.Cancelled, jobs[target].Status);
        Assert.Equal(Now, jobs[target].FinishedAt);
        Assert.Null(jobs[target].IdempotencyKey);
        Assert.Equal(QueueJobStatus.Processing, jobs[processing].Status);
        Assert.All([otherSubject, otherType, resent], id => Assert.Equal(QueueJobStatus.Pending, jobs[id].Status));
        Assert.Equal(Key, jobs[resent].IdempotencyKey);
    }

    [Fact]
    public async Task Cancelamento_e_recusado_se_o_trabalho_foi_pego_antes_de_o_caso_de_uso_salvar()
    {
        await using var db = await MigratedAsync();
        var queue = new JobQueue(db, Clock);
        var id = await queue.EnqueueAsync(Request(), Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1, await queue.CancelPendingAsync(Invite, Subject, Ct));
        // A captura do trabalhador (§6.1) conta a tentativa; Attempts é o token de concorrência (§6.6).
        await db.Database.ExecuteSqlAsync($"""UPDATE "QueueJobs" SET "Status" = 'Processing', "Attempts" = "Attempts" + 1 WHERE "Id" = {id}""", Ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Gravacao_de_quem_leu_o_trabalho_pendente_e_recusada_se_o_cancelamento_salvou_antes()
    {
        await using var db = await MigratedAsync();
        var id = await new JobQueue(db, Clock).EnqueueAsync(Request(), Ct);
        await db.SaveChangesAsync(Ct);
        await using var worker = NewContext();
        var read = await worker.QueueJobs.SingleAsync(j => j.Id == id, Ct); // leu o pendente antes do cancelamento
        await using var useCase = NewContext();
        Assert.Equal(1, await new JobQueue(useCase, Clock).CancelPendingAsync(Invite, Subject, Ct));
        await useCase.SaveChangesAsync(Ct);

        // Sentido inverso: Status também é token de concorrência (§6.6), então o cancelado não volta a Processing.
        worker.Entry(read).Property(j => j.Status).CurrentValue = QueueJobStatus.Processing;
        worker.Entry(read).Property(j => j.Attempts).CurrentValue = 1;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.SaveChangesAsync(Ct));
        Assert.Equal(QueueJobStatus.Cancelled, await db.QueueJobs.Where(j => j.Id == id).Select(j => j.Status).SingleAsync(Ct));
    }

    [Fact]
    public void Porta_da_fila_e_registrada_pela_composicao_da_infraestrutura()
    {
        using var provider = new ServiceCollection()
            .AddDomainPersistence(new ConfigurationBuilder().Build()).AddInfrastructureServices().BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<JobQueue>(scope.ServiceProvider.GetRequiredService<IJobQueue>());
    }
}
