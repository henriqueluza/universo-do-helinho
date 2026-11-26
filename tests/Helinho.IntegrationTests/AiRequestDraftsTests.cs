using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #258: pedir rascunhos em lote (ADR-0004 §10.2 e §13). Sem host web: o caso de uso, o contexto e o banco.</summary>
[Collection(PostgresCollection.Name)]
public class AiRequestDraftsTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 30, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bia = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
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

    /// <summary>Simula o outro clique chegando no meio deste: antes ou depois de a fila conferir a chave.</summary>
    private sealed class RacingQueue(IJobQueue inner, Func<Task> other, bool beforeEnqueue) : IJobQueue
    {
        public async Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
        {
            await (beforeEnqueue ? other() : Task.CompletedTask);
            var id = await inner.EnqueueAsync(request, cancellationToken);
            await (beforeEnqueue ? Task.CompletedTask : other());
            return id;
        }

        public Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken) =>
            inner.CancelPendingAsync(type, subjectId, cancellationToken);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new TestUser(null, null));

    private RequestAiDrafts UseCase(
        AppDbContext db, Guid? userId, string? role = RoleNames.Admin, string provider = AiOptions.Fake, int perAdmin = 60, int global = 300,
        Func<IJobQueue, IJobQueue>? queue = null)
    {
        IJobQueue jobs = new JobQueue(db, _clock);
        var options = new AiOptions { Provider = provider, DailyQuotaPerAdmin = perAdmin, DailyGlobalLimit = global };
        return new RequestAiDrafts(db, queue?.Invoke(jobs) ?? jobs, _clock, new TestUser(userId, role), Options.Create(options));
    }

    private static async Task<(int Jobs, int Queued)> CountAsync(AppDbContext db) =>
        (await db.AiGenerationJobs.CountAsync(Ct), await db.QueueJobs.CountAsync(Ct));

    [Fact]
    public async Task Com_a_IA_desligada_ou_sem_perfil_de_administrador_nada_e_gravado()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);

        var off = await UseCase(db, Ana, provider: AiOptions.Off).HandleAsync(QuizType.Cultural, AgeBand.Age8To10, 5, null, Ct);
        var student = await UseCase(db, Ana, RoleNames.Student).HandleAsync(QuizType.Cultural, AgeBand.Age8To10, 5, null, Ct);
        var anonymous = await UseCase(db, null).HandleAsync(QuizType.Cultural, AgeBand.Age8To10, 5, null, Ct);

        Assert.Equal(
            [RequestAiDraftsStatus.Disabled, RequestAiDraftsStatus.Forbidden, RequestAiDraftsStatus.Forbidden],
            new[] { off.Status, student.Status, anonymous.Status });
        Assert.Equal((0, 0), await CountAsync(db));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(21, null)]
    [InlineData(5, "fale com fulano@exemplo.test")]
    [InlineData(5, "turma do telefone 119876")]
    [InlineData(5, "assunto sintético muito comprido, assunto sintético muito comprido, assunto sintético muito comprido, assunto sintético muito comprido")]
    public async Task Pedido_invalido_e_recusado_sem_repetir_o_que_foi_digitado_e_nada_e_gravado(int count, string? subject)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var useCase = UseCase(db, Ana, RoleNames.Master);

        var error = await Assert.ThrowsAsync<DomainException>(() => useCase.HandleAsync(QuizType.Cultural, AgeBand.Age8To10, count, subject, Ct));
        await Assert.ThrowsAsync<DomainException>(() => useCase.HandleAsync((QuizType)99, AgeBand.Age8To10, 5, null, Ct));

        Assert.True(subject is null || !error.Message.Contains(subject, StringComparison.Ordinal));
        Assert.Equal((0, 0), await CountAsync(db));
    }

    [Fact]
    public async Task Pedido_valido_grava_o_pedido_e_enfileira_o_trabalho_so_com_o_id_dele()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);

        var result = await UseCase(db, Ana, RoleNames.Master).HandleAsync(QuizType.Cultural, AgeBand.Age8To10, 5, "  Festas \n  juninas ", Ct);

        await using var read = NewContext();
        var job = await read.AiGenerationJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((RequestAiDraftsStatus.Accepted, job.Id), (result.Status, result.JobId.GetValueOrDefault()));
        Assert.Equal(
            (AiJobKind.Batch, AiJobStatus.Pending, QuizType.Cultural, AgeBand.Age8To10, 5, "Festas juninas", Ana, T0),
            (job.Kind, job.Status, job.QuizType, job.AgeBand, job.RequestedCount, job.Subject, job.RequestedByUserId, job.CreatedAt));
        var json = await read.Database.SqlQuery<string>($"""SELECT row_to_json(q)::text AS "Value" FROM "QueueJobs" q""").SingleAsync(Ct);
        var row = JsonDocument.Parse(json).RootElement;
        Assert.Equal(
            (RequestAiDrafts.JobType, Ana, "Pending", T0.AddMinutes(10)),
            (row.GetProperty("Type").GetString(), row.GetProperty("SubjectId").GetGuid(), row.GetProperty("Status").GetString(),
                row.GetProperty("ExpiresAt").GetDateTimeOffset()));
        Assert.Equal(["v", "aiJobId"], row.GetProperty("Payload").EnumerateObject().Select(p => p.Name)); // e mais nada (§10.2)
        Assert.Equal((1, job.Id), (row.GetProperty("Payload").GetProperty("v").GetInt32(), row.GetProperty("Payload").GetProperty("aiJobId").GetGuid()));
        Assert.StartsWith($"ai.question-drafts:{Ana:N}:Cultural-Age8To10-5-", row.GetProperty("IdempotencyKey").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Festas", json, StringComparison.OrdinalIgnoreCase); // o assunto não entra na fila
    }

    [Fact]
    public async Task Cota_por_pessoa_soma_as_sugestoes_pedidas_em_24_horas_inclusive_as_que_falharam()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var ana = UseCase(db, Ana, perAdmin: 10);
        Assert.Equal(RequestAiDraftsStatus.Accepted, (await ana.HandleAsync(QuizType.Cultural, AgeBand.Age8To10, 6, null, Ct)).Status);
        (await db.AiGenerationJobs.SingleAsync(Ct)).Fail("ai_timeout", T0.AddMinutes(2)); // pedido que falhou também conta
        await db.SaveChangesAsync(Ct);

        _clock.UtcNow = T0.AddHours(1);
        var refused = await ana.HandleAsync(QuizType.Cultural, AgeBand.Age11To12, 5, null, Ct);
        var tooBig = await ana.HandleAsync(QuizType.Cultural, AgeBand.Age11To12, 11, null, Ct);
        Assert.Equal((1, 1), await CountAsync(db)); // o pedido recusado não foi gravado
        var bia = await UseCase(db, Bia, perAdmin: 10).HandleAsync(QuizType.Cultural, AgeBand.Age11To12, 5, null, Ct);
        var fits = await ana.HandleAsync(QuizType.Cultural, AgeBand.Age11To12, 4, null, Ct);
        _clock.UtcNow = T0.AddHours(24).AddSeconds(1);
        var later = await ana.HandleAsync(QuizType.Cultural, AgeBand.Age13To14, 6, null, Ct);

        Assert.Equal((RequestAiDraftsStatus.QuotaExceeded, null), (refused.Status, refused.JobId));
        Assert.Equal(new AiQuotaExceeded(Global: false, Limit: 10, Remaining: 4, AvailableAt: T0.AddHours(24)), refused.Quota);
        Assert.Equal(new AiQuotaExceeded(Global: false, Limit: 10, Remaining: 4, AvailableAt: null), tooBig.Quota); // maior que o próprio limite
        Assert.Equal(
            [RequestAiDraftsStatus.Accepted, RequestAiDraftsStatus.Accepted, RequestAiDraftsStatus.Accepted],
            new[] { bia.Status, fits.Status, later.Status });
    }

    [Fact]
    public async Task Teto_global_soma_os_pedidos_de_todos()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        Assert.Equal(RequestAiDraftsStatus.Accepted, (await UseCase(db, Ana, global: 8).HandleAsync(QuizType.Esportivo, AgeBand.Age6To7, 5, null, Ct)).Status);
        _clock.UtcNow = T0.AddHours(1);
        Assert.Equal(RequestAiDraftsStatus.Accepted, (await UseCase(db, Bia, global: 8).HandleAsync(QuizType.Esportivo, AgeBand.Age6To7, 3, null, Ct)).Status);

        _clock.UtcNow = T0.AddHours(2);
        var refused = await UseCase(db, Guid.NewGuid(), RoleNames.Master, global: 8).HandleAsync(QuizType.Esportivo, AgeBand.Age6To7, 4, null, Ct);

        Assert.Equal(RequestAiDraftsStatus.QuotaExceeded, refused.Status);
        Assert.Equal(new AiQuotaExceeded(Global: true, Limit: 8, Remaining: 0, AvailableAt: T0.AddHours(24)), refused.Quota);
        Assert.Equal((2, 2), await CountAsync(db));
    }

    [Fact]
    public async Task Clique_duplo_leva_ao_pedido_ja_criado_sem_gastar_cota_e_pedido_diferente_e_aceito()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        var ana = UseCase(db, Ana, perAdmin: 12);

        var first = await ana.HandleAsync(QuizType.Ambiental, AgeBand.Age8To10, 5, "Reciclagem", Ct);
        _clock.UtcNow = T0.AddSeconds(60); // mesma janela de 2 minutos
        var again = await ana.HandleAsync(QuizType.Ambiental, AgeBand.Age8To10, 5, "  Reciclagem ", Ct);
        Assert.Equal((1, 1), await CountAsync(db));
        var otherSubject = await ana.HandleAsync(QuizType.Ambiental, AgeBand.Age8To10, 5, "Água", Ct);
        _clock.UtcNow = T0.AddSeconds(90); // janela seguinte: é um pedido novo, e a cota (12) já não cabe
        var nextWindow = await ana.HandleAsync(QuizType.Ambiental, AgeBand.Age8To10, 5, "Reciclagem", Ct);

        Assert.Equal((RequestAiDraftsStatus.Accepted, RequestAiDraftsStatus.AlreadyRequested, first.JobId), (first.Status, again.Status, again.JobId));
        Assert.Equal((RequestAiDraftsStatus.Accepted, RequestAiDraftsStatus.QuotaExceeded), (otherSubject.Status, nextWindow.Status));
        Assert.Equal((2, 2), await CountAsync(db));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dois_cliques_ao_mesmo_tempo_criam_um_pedido_so(bool otherSavesBeforeTheQueueChecks)
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        await using var otherDb = NewContext();
        RequestAiDraftsResult? winner = null;
        async Task OtherClickAsync() => winner = await UseCase(otherDb, Ana).HandleAsync(QuizType.Diversidade, AgeBand.Age13To14, 3, null, Ct);

        var loser = await UseCase(db, Ana, queue: inner => new RacingQueue(inner, OtherClickAsync, otherSavesBeforeTheQueueChecks))
            .HandleAsync(QuizType.Diversidade, AgeBand.Age13To14, 3, null, Ct);

        Assert.Equal(RequestAiDraftsStatus.Accepted, winner?.Status);
        Assert.Equal((RequestAiDraftsStatus.AlreadyRequested, winner?.JobId), (loser.Status, loser.JobId));
        await using var read = NewContext();
        Assert.Equal((1, 1), await CountAsync(read)); // a gravação recusada não deixou pedido sem trabalho
    }
}
