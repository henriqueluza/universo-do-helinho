using System.Text.Json;
using Helinho.Application.Abstractions;

namespace Helinho.UnitTests;

/// <summary>Contratos do tratador da fila (ADR-0003 §6.6 e §6.7).</summary>
public class JobHandlerContractsTests
{
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(15);

    private sealed class FakeHandler : IJobHandler, IAbandonedJobHandler
    {
        public string? Abandoned { get; private set; }

        public IReadOnlyCollection<string> Types => ["test.fake"];

        public JobPolicy Policy => new(
            MaxAttempts: 8, MinBackoff: TimeSpan.FromMinutes(1), MaxBackoff: TimeSpan.FromHours(1),
            BatchSize: 10, ItemTimeout: TimeSpan.FromSeconds(10), Lease: TimeSpan.FromMinutes(2));

        public Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken) =>
            Task.FromResult<JobResult>(job.Attempts > 1 ? new JobResult.Fail("subject_missing") : new JobResult.Succeeded("id-externo"));

        public Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken)
        {
            Abandoned = reason;
            return Task.CompletedTask;
        }
    }

    private static JobContext Context(int attempts) =>
        new(Guid.NewGuid(), "test.fake", Guid.NewGuid(), JsonDocument.Parse("""{"v":1}""").RootElement, attempts,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1));

    [Fact]
    public async Task Tratador_recebe_o_contexto_e_devolve_um_dos_quatro_resultados()
    {
        var handler = new FakeHandler();
        var ct = TestContext.Current.CancellationToken;

        var first = await handler.HandleAsync(Context(attempts: 1), ct);
        var second = await handler.HandleAsync(Context(attempts: 2), ct);
        await handler.OnAbandonedAsync(Context(attempts: 8), "max_attempts", ct);

        Assert.Equal("id-externo", Assert.IsType<JobResult.Succeeded>(first).ExternalId);
        Assert.Equal("subject_missing", Assert.IsType<JobResult.Fail>(second).ErrorCode);
        Assert.Equal("max_attempts", handler.Abandoned);
        Assert.Equal(TimeSpan.FromMinutes(2), handler.Policy.Lease);
        // Só as quatro formas do ADR §6.6: o construtor privado impede uma quinta fora do tipo.
        Assert.Equal(
            ["Fail", "Retry", "Succeeded", "Suspend"],
            typeof(JobResult).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(JobResult))).Select(t => t.Name).Order());
        Assert.DoesNotContain(typeof(JobResult).GetConstructors(), c => c.IsPublic);
    }

    [Fact]
    public void Codigos_do_vocabulario_fechado_sao_aceitos()
    {
        var notBefore = DateTimeOffset.UnixEpoch.AddMinutes(5);

        Assert.Equal(("http_429", notBefore), (new JobResult.Retry("http_429", notBefore).ErrorCode, new JobResult.Retry("http_429", notBefore).NotBefore));
        Assert.Equal(("http_402", Pause), (new JobResult.Suspend("http_402", Pause).ErrorCode, new JobResult.Suspend("http_402", Pause).PauseFor));
        Assert.Null(new JobResult.Succeeded().ExternalId);
        Assert.Null(new JobResult.Retry("timeout").NotBefore);
    }

    [Theory]
    [InlineData("responsavel@example.org")]
    [InlineData("Falha ao enviar para Maria Sintética")]
    [InlineData("Timeout")]
    [InlineData("")]
    [InlineData("codigo-com-mais-de-trinta-caracteres")]
    [InlineData("https://example.org/conta/definir-senha?token=abc")]
    public void Mensagem_livre_nao_vira_codigo_de_erro(string text)
    {
        Assert.Throws<ArgumentException>(() => new JobResult.Fail(text));
        Assert.Throws<ArgumentException>(() => new JobResult.Retry(text));
        Assert.Throws<ArgumentException>(() => new JobResult.Suspend(text, Pause));
    }
}
