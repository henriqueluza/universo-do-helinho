using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #277: retorno de entrega gravado em <c>QueueJobs</c> por <c>ExternalId</c> (ADR-0003 §12). Sem host web: só o
/// contexto e o banco, um por classe. Dados sintéticos.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EmailFeedbackRecorderTests(PostgresFixture postgres, EmailFeedbackRecorderTests.Banco banco)
    : IClassFixture<EmailFeedbackRecorderTests.Banco>, IClock
{
    private const string Acesso = "email.student-access";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    public sealed class Banco
    {
        public string Nome { get; } = $"retorno_{Guid.NewGuid():N}";

        public bool Migrado { get; set; }
    }

    private async Task<AppDbContext> ContextoAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = banco.Nome }.ConnectionString)
                .Options, new AnonymousCurrentUser());
        if (!banco.Migrado)
        {
            await db.Database.MigrateAsync(Ct);
            banco.Migrado = true;
        }

        return db;
    }

    private static string NovoMessageId() => $"{Guid.NewGuid():N}@relay.example.org";

    private async Task<Guid> TrabalhoEnviadoAsync(AppDbContext db, string externalId, string type = Acesso)
    {
        var job = new QueueJob(new JobRequest(type, Guid.NewGuid(), new JobPayload(), TimeSpan.FromHours(72)), UtcNow);
        db.QueueJobs.Add(job);
        await db.SaveChangesAsync(Ct);
        // O ExternalId é gravado pelo trabalhador (Issues 8a e 8b do plano); aqui, direto no banco.
        await db.QueueJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ExternalId, externalId), Ct);
        return job.Id;
    }

    private static async Task<(string? Feedback, DateTimeOffset? At)> RetornoAsync(AppDbContext db, Guid jobId)
    {
        var job = await db.QueueJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, Ct);
        return (job.Feedback, job.FeedbackAt);
    }

    [Theory]
    [InlineData(EmailFeedback.HardBounce)]
    [InlineData(EmailFeedback.InvalidEmail)]
    [InlineData(EmailFeedback.Blocked)]
    [InlineData(EmailFeedback.Spam)]
    public async Task Cada_um_dos_quatro_eventos_grava_o_retorno_so_no_trabalho_daquele_id(string evento)
    {
        await using var db = await ContextoAsync();
        var messageId = NovoMessageId();
        var job = await TrabalhoEnviadoAsync(db, messageId);
        var outro = await TrabalhoEnviadoAsync(db, NovoMessageId());

        var gravado = await new EmailFeedbackRecorder(db, this).RecordAsync(evento, $"<{messageId}>", Ct); // comparado sem os sinais (§12)

        Assert.Equal(job, gravado);
        Assert.Equal((evento, UtcNow), await RetornoAsync(db, job));
        Assert.Equal((null, null), await RetornoAsync(db, outro));
    }

    [Fact]
    public async Task O_mesmo_evento_de_novo_nao_muda_o_estado_e_o_primeiro_retorno_vale()
    {
        await using var db = await ContextoAsync();
        var messageId = NovoMessageId();
        var job = await TrabalhoEnviadoAsync(db, messageId);
        var recorder = new EmailFeedbackRecorder(db, this);
        await recorder.RecordAsync(EmailFeedback.Spam, messageId, Ct);
        var primeiro = await RetornoAsync(db, job);
        UtcNow += TimeSpan.FromMinutes(10);

        Assert.Null(await recorder.RecordAsync(EmailFeedback.Spam, messageId, Ct));
        Assert.Null(await recorder.RecordAsync(EmailFeedback.HardBounce, messageId, Ct));

        Assert.Equal(EmailFeedback.Spam, primeiro.Feedback);
        Assert.Equal(primeiro, await RetornoAsync(db, job));
    }

    [Theory]
    [InlineData("delivered", "ID", Acesso)]
    [InlineData("soft_bounce", "ID", Acesso)]
    [InlineData("deferred", "ID", Acesso)]
    [InlineData("error", "ID", Acesso)]
    [InlineData("HARD_BOUNCE", "ID", Acesso)]
    [InlineData("", "ID", Acesso)]
    [InlineData(EmailFeedback.HardBounce, "desconhecido-ID", Acesso)]
    [InlineData(EmailFeedback.HardBounce, "", Acesso)]
    [InlineData(EmailFeedback.HardBounce, "<>", Acesso)]
    [InlineData(EmailFeedback.HardBounce, "ID", "ai.question-drafts")] // ExternalId de outro provedor
    public async Task Evento_fora_da_lista_id_desconhecido_e_trabalho_que_nao_e_de_email_nao_mudam_nada(string evento, string id, string tipo)
    {
        await using var db = await ContextoAsync();
        var messageId = NovoMessageId();
        var job = await TrabalhoEnviadoAsync(db, messageId, tipo);

        var gravado = await new EmailFeedbackRecorder(db, this).RecordAsync(evento, id.Replace("ID", messageId, StringComparison.Ordinal), Ct);

        Assert.Null(gravado);
        Assert.Equal((null, null), await RetornoAsync(db, job));
    }

    [Fact]
    public void O_id_da_mensagem_perde_os_sinais_e_nao_passa_do_tamanho_da_coluna()
    {
        Assert.Equal("1@relay.example.org", EmailFeedback.NormalizeMessageId(" <1@relay.example.org> "));
        Assert.Equal(200, EmailFeedback.NormalizeMessageId(new string('a', 200))?.Length);
        Assert.Null(EmailFeedback.NormalizeMessageId(new string('a', 201)));
        Assert.Null(EmailFeedback.NormalizeMessageId(null));
    }
}
