using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Issue #275: leitura do pedido de IA e conferência de pedido vencido (ADR-0004 §10.2). Sem host web.</summary>
[Collection(PostgresCollection.Name)]
public class AiGetDraftJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly AiJobUsage Usage = new("Fake", "fake-v1", "gerar-questoes.v1", 10, 20, TimeSpan.FromSeconds(1));
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

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = _database }.ConnectionString)
            .Options, new TestUser(null, null));

    private GetAiDraftJob UseCase(AppDbContext db, string? role = RoleNames.Admin, string provider = AiOptions.Fake) =>
        new(db, _clock, new TestUser(role is null ? null : Ana, role), Options.Create(new AiOptions { Provider = provider }));

    private static AiGenerationJob NewJob(AiJobKind kind = AiJobKind.Batch) =>
        new(kind, QuizType.Cultural, AgeBand.Age8To10, kind == AiJobKind.Batch ? 3 : 1, kind == AiJobKind.Batch ? "Festas juninas" : null, Ana, T0);

    private async Task<AppDbContext> SeededAsync(params object[] entities)
    {
        var db = NewContext();
        await db.Database.MigrateAsync(Ct);
        db.AddRange(entities);
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Fact]
    public async Task Leitura_devolve_estado_contadores_e_as_sugestoes_em_ordem_com_as_revisadas()
    {
        var job = NewJob();
        job.Start(T0.AddSeconds(1));
        job.Succeed(Usage, new AiJobCounts(4, 3, 1, 0), T0.AddSeconds(9));
        var drafts = Enumerable.Range(0, 3)
            .Select(i => new AiQuestionSuggestion(job.Id, i, $"Qual é a sugestão sintética {i}?", ["Um", "Dois", "Três", "Quatro"], i, "Explicação sintética.")).ToList();
        drafts[2].Discard(Ana, T0.AddMinutes(1));
        await using var db = await SeededAsync(job, drafts[2], drafts[0], drafts[1]);
        _clock.UtcNow = T0.AddDays(3); // pedido concluído não vence

        var result = await UseCase(db, RoleNames.Master).HandleAsync(job.Id, Ct);

        var view = Assert.IsType<AiDraftJobView>(result.Job);
        Assert.Equal(GetAiDraftJobStatus.Found, result.Status);
        Assert.Equal(
            (job.Id, QuizType.Cultural, AgeBand.Age8To10, 3, "Festas juninas", AiJobStatus.Succeeded, null, T0, T0.AddSeconds(9)),
            (view.Id, view.QuizType, view.AgeBand, view.RequestedCount, view.Subject, view.Status, view.FailureReason, view.CreatedAt, view.FinishedAt));
        Assert.Equal((4, 3, 1, 0, 1), (view.GeneratedCount, view.ValidCount, view.InvalidCount, view.DuplicateCount, view.ReviewedCount));
        Assert.Equal([0, 1, 2], view.Drafts.Select(d => d.Position));
        Assert.Equal([AiSuggestionStatus.Pending, AiSuggestionStatus.Pending, AiSuggestionStatus.Discarded], view.Drafts.Select(d => d.Status));
        Assert.Equal((drafts[1].Id, "Qual é a sugestão sintética 1?", 1, "Explicação sintética.", null),
            (view.Drafts[1].Id, view.Drafts[1].Statement, view.Drafts[1].CorrectIndex, view.Drafts[1].Explanation, view.Drafts[1].QuestionId));
        Assert.Equal(["Um", "Dois", "Três", "Quatro"], view.Drafts[1].Options);
    }

    [Fact]
    public async Task Com_a_IA_desligada_sem_perfil_ou_sem_pedido_em_lote_nada_e_devolvido()
    {
        var (batch, single) = (NewJob(), NewJob(AiJobKind.Single));
        await using var db = await SeededAsync(batch, single);

        var off = await UseCase(db, provider: AiOptions.Off).HandleAsync(batch.Id, Ct);
        var student = await UseCase(db, RoleNames.Student).HandleAsync(batch.Id, Ct);
        var anonymous = await UseCase(db, role: null).HandleAsync(batch.Id, Ct);
        var unknown = await UseCase(db).HandleAsync(Guid.NewGuid(), Ct);
        var notBatch = await UseCase(db).HandleAsync(single.Id, Ct);

        Assert.Equal(
            [GetAiDraftJobStatus.Disabled, GetAiDraftJobStatus.Forbidden, GetAiDraftJobStatus.Forbidden, GetAiDraftJobStatus.NotFound, GetAiDraftJobStatus.NotFound],
            new[] { off, student, anonymous, unknown, notBatch }.Select(r => r.Status));
        Assert.All(new[] { off, student, anonymous, unknown, notBatch }, r => Assert.Null(r.Job));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pedido_nao_concluido_alem_da_validade_e_lido_como_vencido_e_a_transicao_e_gravada(bool started)
    {
        var job = NewJob();
        if (started)
        {
            job.Start(T0.AddSeconds(1));
        }

        await using var db = await SeededAsync(job);
        var useCase = UseCase(db);

        _clock.UtcNow = T0.AddMinutes(10).AddSeconds(90); // validade da fila (10 min) + prazo da chamada (90 s): ainda pode concluir
        var inTime = await useCase.HandleAsync(job.Id, Ct);
        _clock.UtcNow = T0.AddMinutes(10).AddSeconds(91);
        var expired = await useCase.HandleAsync(job.Id, Ct);

        Assert.Equal(started ? AiJobStatus.Processing : AiJobStatus.Pending, inTime.Job?.Status);
        Assert.Equal((AiJobStatus.Failed, AiFailureCodes.Expired, _clock.UtcNow), (expired.Job?.Status, expired.Job?.FailureReason, expired.Job?.FinishedAt));
        await using var read = NewContext();
        var saved = await read.AiGenerationJobs.AsNoTracking().SingleAsync(Ct);
        Assert.Equal((AiJobStatus.Failed, "expired", T0.AddMinutes(10).AddSeconds(91)), (saved.Status, saved.FailureReason, saved.FinishedAt));
    }

    [Fact]
    public async Task Conferencia_de_vencido_nao_desfaz_a_conclusao_do_trabalhador_nem_muda_outro_pedido()
    {
        var (succeeded, failed, other) = (NewJob(), NewJob(), NewJob());
        succeeded.Succeed(Usage, new AiJobCounts(3, 3, 0, 0), T0.AddMinutes(11));
        failed.Fail(AiFailureCodes.Timeout, T0.AddSeconds(90));
        await using var db = await SeededAsync(succeeded, failed, other);
        _clock.UtcNow = T0.AddHours(1);

        var first = (await UseCase(db).HandleAsync(succeeded.Id, Ct)).Job;
        var second = (await UseCase(db).HandleAsync(failed.Id, Ct)).Job;

        Assert.Equal((AiJobStatus.Succeeded, null, T0.AddMinutes(11)), (first?.Status, first?.FailureReason, first?.FinishedAt));
        Assert.Equal((AiJobStatus.Failed, "ai_timeout", T0.AddSeconds(90)), (second?.Status, second?.FailureReason, second?.FinishedAt));
        await using var read = NewContext();
        Assert.Equal(AiJobStatus.Pending, (await read.AiGenerationJobs.AsNoTracking().SingleAsync(j => j.Id == other.Id, Ct)).Status); // só o pedido lido é conferido
    }
}
