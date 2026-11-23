using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Persistence;

namespace Helinho.UnitTests;

/// <summary>Como um trabalho sai do processamento (ADR-0003 §6.2, §6.3 e §6.7): a decisão, sem banco.</summary>
public class JobTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobPolicy Email = new(8, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60), 10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2));
    private static readonly JobPolicy Ai = new(2, TimeSpan.Zero, TimeSpan.Zero, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150));

    private static JobTransition After(JobResult result, int attempts = 1, double jitter = 0, double expiresInMinutes = 1440, JobPolicy? policy = null) =>
        JobTransition.After(result, attempts, Now.AddMinutes(expiresInMinutes), policy ?? Email, Now, jitter);

    [Fact]
    public void Sucesso_guarda_o_id_externo_e_erro_permanente_vai_direto_para_Failed()
    {
        Assert.Equal(new JobTransition(QueueJobStatus.Succeeded, ExternalId: "msg-1"), After(new JobResult.Succeeded("msg-1")));
        Assert.Equal(new JobTransition(QueueJobStatus.Failed, "subject_missing"), After(new JobResult.Fail("subject_missing")));
    }

    [Theory]
    [InlineData(1, 0, 60)]
    [InlineData(2, 0, 120)]
    [InlineData(3, 0, 240)]
    [InlineData(6, 0, 1920)]
    [InlineData(7, 0, 3600)] // teto de 60 minutos
    [InlineData(1, -0.2, 48)]
    [InlineData(1, 0.2, 72)]
    [InlineData(7, 0.2, 4320)] // a variação incide sobre a espera já limitada
    [InlineData(1, 5, 72)] // variação fora da faixa é limitada a ±20%
    public void Repetir_espera_o_dobro_a_cada_tentativa_com_teto_e_variacao_de_20_por_cento(int attempts, double jitter, int seconds)
    {
        var transition = After(new JobResult.Retry("http_500"), attempts, jitter);

        Assert.Equal(new JobTransition(QueueJobStatus.Pending, "http_500", Now.AddSeconds(seconds)), transition);
    }

    [Fact]
    public void Repetir_respeita_o_NotBefore_do_tratador_quando_ele_e_posterior_a_espera()
    {
        Assert.Equal(Now.AddMinutes(30), After(new JobResult.Retry("http_429", Now.AddMinutes(30))).NextAttemptAt);
        Assert.Equal(Now.AddMinutes(1), After(new JobResult.Retry("http_429", Now.AddSeconds(5))).NextAttemptAt);
    }

    [Fact]
    public void Repetir_na_ultima_tentativa_encerra_com_max_attempts()
    {
        var abandoned = new JobTransition(QueueJobStatus.Failed, JobTransition.MaxAttempts, Abandoned: true);

        Assert.Equal(abandoned, After(new JobResult.Retry("http_500"), attempts: 8));
        Assert.Equal(abandoned, After(new JobResult.Retry("timeout"), attempts: 2, policy: Ai));
        Assert.Equal(QueueJobStatus.Pending, After(new JobResult.Retry("timeout"), attempts: 1, policy: Ai).Status);
    }

    [Fact]
    public void Repetir_cuja_espera_cairia_depois_da_validade_encerra_com_expired()
    {
        var abandoned = new JobTransition(QueueJobStatus.Failed, JobTransition.Expired, Abandoned: true);

        Assert.Equal(abandoned, After(new JobResult.Retry("http_500"), attempts: 3, expiresInMinutes: 3)); // espera de 4 minutos
        Assert.Equal(abandoned, After(new JobResult.Retry("http_429", Now.AddMinutes(31)), expiresInMinutes: 30));
        Assert.Equal(QueueJobStatus.Pending, After(new JobResult.Retry("http_500"), attempts: 3, expiresInMinutes: 4).Status);
    }

    [Fact]
    public void Suspender_volta_a_pendente_devolvendo_a_tentativa_e_nao_e_encerramento()
    {
        var suspended = new JobTransition(QueueJobStatus.Pending, "http_402", Now.AddMinutes(15), ReturnsAttempt: true);

        Assert.Equal(suspended, After(new JobResult.Suspend("http_402", TimeSpan.FromMinutes(15)), attempts: 8)); // não gasta o teto
        Assert.Equal(suspended, After(new JobResult.Suspend("http_402", TimeSpan.FromMinutes(15)), expiresInMinutes: 5)); // só expira ao ser pego
    }

    [Fact]
    public void Ao_pegar_o_item_a_fila_encerra_o_vencido_e_o_que_passou_do_teto()
    {
        var expires = Now.AddHours(1);

        Assert.Null(JobTransition.WhenCaptured(8, expires, Email, Now));
        Assert.Equal(JobTransition.MaxAttempts, JobTransition.WhenCaptured(9, expires, Email, Now)?.Error); // o processo anterior morreu
        Assert.Equal(JobTransition.MaxAttempts, JobTransition.WhenCaptured(3, expires, Ai, Now)?.Error);
        var expired = JobTransition.WhenCaptured(9, expires, Email, expires); // a validade vem antes do teto
        Assert.Equal(new JobTransition(QueueJobStatus.Failed, JobTransition.Expired, Abandoned: true), expired);
    }

    [Fact]
    public void So_um_trabalho_em_processamento_recebe_o_resultado()
    {
        var pending = new QueueJob(new JobRequest("email.admin-invite", Guid.NewGuid(), new JobPayload(), TimeSpan.FromHours(24)), Now);

        Assert.Throws<InvalidOperationException>(() => pending.Apply(After(new JobResult.Succeeded()), Now));
        Assert.Equal((QueueJobStatus.Pending, 0), (pending.Status, pending.Attempts));
    }
}
