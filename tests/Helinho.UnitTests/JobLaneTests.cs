using Helinho.Application.Abstractions;

namespace Helinho.UnitTests;

/// <summary>Raias do trabalhador (ADR-0003 §6.1): uma por registro de tratador, conferidas na subida.</summary>
public class JobLaneTests
{
    private const string Invite = "email.admin-invite";
    private const string Access = "email.student-access";
    private static readonly JobPolicy Email = new(8, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60), 10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2));
    private static readonly JobPolicy Ai = new(2, TimeSpan.Zero, TimeSpan.Zero, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150));

    private sealed class Stub(string[] types, JobPolicy policy) : IJobHandler
    {
        public IReadOnlyCollection<string> Types => types;

        public JobPolicy Policy => policy;

        public Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void Cada_registro_vira_uma_raia_com_os_tipos_e_a_politica_dele()
    {
        // O mesmo tratador registrado duas vezes (o caso do e-mail, §15) e o de IA do ADR-0004: 10 × 10 s e 1 × 90 s cabem na posse.
        var lanes = JobLane.From([new Stub([Invite], Email), new Stub([Access], Email), new Stub(["ai.question-drafts"], Ai)]);

        Assert.Equal([Invite, Access, "ai.question-drafts"], lanes.Select(l => Assert.Single(l.Types)));
        Assert.Equal([Email, Email, Ai], lanes.Select(l => l.Policy));
        Assert.Empty(JobLane.From([]));
    }

    [Theory]
    [InlineData(13, 10, 120)] // 130 s de lote não cabem em 2 minutos de posse
    [InlineData(1, 90, 89)]
    [InlineData(0, 10, 120)]
    [InlineData(1, 0, 120)]
    public void Posse_que_nao_cobre_o_lote_inteiro_impede_a_subida(int batchSize, int itemSeconds, int leaseSeconds)
    {
        var policy = Email with { BatchSize = batchSize, ItemTimeout = TimeSpan.FromSeconds(itemSeconds), Lease = TimeSpan.FromSeconds(leaseSeconds) };

        var error = Assert.Throws<InvalidOperationException>(() => JobLane.From([new Stub([Invite], policy)]));

        Assert.Contains($"[{Invite}]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tipo_atendido_por_dois_registros_ou_registro_sem_tipo_impede_a_subida()
    {
        var twice = Assert.Throws<InvalidOperationException>(() => JobLane.From([new Stub([Invite, Access], Email), new Stub([Access], Email)]));

        Assert.Contains($"mais de um tratador registrado para [{Access}]", twice.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => JobLane.From([new Stub([], Email)]));
    }
}
