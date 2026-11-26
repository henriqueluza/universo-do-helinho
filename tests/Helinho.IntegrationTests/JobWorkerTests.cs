using System.Threading.Channels;
using Helinho.Application.Abstractions;
using Helinho.Web.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Laços do trabalhador da fila (ADR-0003 §6.4) com processador falso e relógio manual: sem banco, sem host e sem espera real.</summary>
public sealed class JobWorkerTests
{
    private const string Invite = "email.admin-invite";
    private const string Access = "email.student-access";
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobPolicy Email = new(8, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60), 2, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2));
    private static readonly JobBatchResult Idle = new(0, null);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

    private readonly ManualTime _time = new(Now);
    private readonly FakeProcessor _processor = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FilaOptions Off => new() { Worker = { Enabled = false } };

    private sealed class Stub(string[] types, JobPolicy? policy = null) : IJobHandler
    {
        public IReadOnlyCollection<string> Types => types;

        public JobPolicy Policy => policy ?? Email;

        public Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeProcessor : IJobProcessor
    {
        public Channel<string> Batches { get; } = Channel.CreateUnbounded<string>();

        public Channel<TimeSpan> Purges { get; } = Channel.CreateUnbounded<TimeSpan>();

        public Func<JobLane, CancellationToken, CancellationToken, Task<JobBatchResult>> OnBatch { get; set; } = (_, _, _) => Task.FromResult(Idle);

        public Task<JobBatchResult> ProcessBatchAsync(JobLane lane, CancellationToken stopping, CancellationToken abort)
        {
            Batches.Writer.TryWrite(lane.Types.First());
            return OnBatch(lane, stopping, abort);
        }

        public Task<int> PurgeFinishedAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken)
        {
            Purges.Writer.TryWrite(retention);
            return Task.FromResult(0);
        }
    }

    private JobWorker Worker(FilaOptions? options = null, params IJobHandler[] handlers)
    {
        var services = new ServiceCollection();
        Array.ForEach(handlers, handler => services.AddScoped(_ => handler)); // um registro por tratador
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new JobWorker(scopes, _processor, Options.Create(options ?? new FilaOptions()), _time, NullLogger<JobWorker>.Instance);
    }

    private async Task<string[]> NextBatchesAsync(int count)
    {
        var lanes = new string[count];
        for (var i = 0; i < count; i++)
        {
            lanes[i] = await _processor.Batches.Reader.ReadAsync(Ct);
        }

        return [.. lanes.Order()];
    }

    /// <summary>Espera os laços chegarem à espera (um temporizador cada) e só então anda o relógio.</summary>
    private async Task AdvanceAsync(TimeSpan by, int waitingLoops)
    {
        for (var i = 0; i < waitingLoops; i++)
        {
            await _time.TimerCreated.WaitAsync(Ct);
        }

        _time.Advance(by);
    }

    [Fact]
    public async Task Cada_registro_do_mesmo_tratador_tem_o_proprio_laco_e_a_propria_pausa()
    {
        using var worker = Worker(null, new Stub([Invite]), new Stub([Access]));
        // Só a raia do convite suspende, na primeira volta, por 15 minutos (§6.3).
        _processor.OnBatch = (lane, _, _) =>
            Task.FromResult(Idle with { PausedUntil = lane.Types.Contains(Invite) && _time.GetUtcNow() == Now ? Now.AddMinutes(15) : null });

        await worker.StartAsync(Ct);

        Assert.Equal([Invite, Access], await NextBatchesAsync(2));
        await AdvanceAsync(Poll, waitingLoops: 3); // a limpeza e as duas raias
        Assert.Equal([Access], await NextBatchesAsync(1)); // fila vazia: nova consulta a cada 5 s; a raia suspensa segue parada
        await AdvanceAsync(Poll, waitingLoops: 1);
        Assert.Equal([Access], await NextBatchesAsync(1));
        await AdvanceAsync(TimeSpan.FromMinutes(15) - Poll - Poll, waitingLoops: 1);
        Assert.Equal([Invite, Access], await NextBatchesAsync(2)); // a pausa acabou
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task Lote_cheio_nao_espera_e_a_falha_de_uma_volta_nao_derruba_o_laco()
    {
        using var worker = Worker(null, new Stub([Invite]));
        var passes = new Queue<Func<JobBatchResult>>([() => new(2, null), () => throw new InvalidOperationException("sintética")]);
        _processor.OnBatch = (_, _, _) => Task.FromResult(passes.TryDequeue(out var pass) ? pass() : Idle);

        await worker.StartAsync(Ct);

        Assert.Equal([Invite, Invite], await NextBatchesAsync(2)); // lote de 2 cheio: a segunda volta veio sem o relógio andar
        await AdvanceAsync(Poll, waitingLoops: 2); // a limpeza e a espera depois da falha
        Assert.Equal([Invite], await NextBatchesAsync(1));
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task Parada_nao_pega_lote_novo_e_so_o_fim_da_janela_do_host_cancela_o_item_em_curso()
    {
        using var worker = Worker(null, new Stub([Invite]));
        var stopSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = false;
        _processor.OnBatch = async (_, stopping, abort) =>
        {
            stopping.Register(stopSeen.SetResult);
            try
            {
                await Task.Delay(Timeout.Infinite, abort); // o item em curso
            }
            finally
            {
                aborted = abort.IsCancellationRequested;
            }

            return Idle;
        };
        await worker.StartAsync(Ct);
        await NextBatchesAsync(1);
        using var window = new CancellationTokenSource();

        var stop = worker.StopAsync(window.Token);
        await stopSeen.Task; // o sinal de parada chegou ao processador...
        Assert.False(stop.IsCompleted || aborted); // ...e, sozinho, não cancela o item
        await window.CancelAsync(); // fim da janela de parada do host
        await stop;
        await Assert.IsAssignableFrom<Task>(worker.ExecuteTask);

        Assert.True(aborted);
        Assert.False(_processor.Batches.Reader.TryRead(out _)); // nenhum lote novo
    }

    [Fact]
    public async Task Parada_depois_do_descarte_nao_lanca()
    {
        using var worker = Worker(Off); // o host de teste é parado por dois caminhos, e o descarte pode vir entre um e outro
        await worker.StartAsync(Ct);
        worker.Dispose();

        Assert.Null(await Record.ExceptionAsync(() => worker.StopAsync(Ct)));
    }

    [Fact]
    public async Task Limpeza_roda_na_subida_e_uma_vez_por_dia_com_a_retencao_configurada()
    {
        using var worker = Worker(new FilaOptions { RetentionDays = 7 });

        await worker.StartAsync(Ct);

        Assert.Equal(TimeSpan.FromDays(7), await _processor.Purges.Reader.ReadAsync(Ct));
        await AdvanceAsync(TimeSpan.FromHours(23), waitingLoops: 1);
        Assert.False(_processor.Purges.Reader.TryRead(out _));
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromDays(7), await _processor.Purges.Reader.ReadAsync(Ct));
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task Desligado_nao_chama_o_processador_mas_a_subida_ainda_recusa_politica_invalida()
    {
        var ai = new JobPolicy(2, TimeSpan.Zero, TimeSpan.Zero, 1, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150));
        using var worker = Worker(Off, new Stub([Invite], Email with { BatchSize = 12 }), new Stub(["ai.question-drafts"], ai)); // 12 × 10 s e 1 × 90 s cabem na posse
        using var shortLease = Worker(Off, new Stub([Invite], Email with { BatchSize = 13 })); // 130 s não cabem em 2 minutos
        using var twice = Worker(Off, new Stub([Invite, Access]), new Stub([Access]));

        await worker.StartAsync(Ct);
        await Assert.IsAssignableFrom<Task>(worker.ExecuteTask);

        Assert.False(_processor.Batches.Reader.TryRead(out _) || _processor.Purges.Reader.TryRead(out _));
        Assert.Equal(0, _time.TimerCreated.CurrentCount); // nenhum laço ficou esperando
        Assert.Contains("prazo de posse", (await Assert.ThrowsAsync<InvalidOperationException>(() => shortLease.StartAsync(Ct))).Message, StringComparison.Ordinal);
        Assert.Contains($"[{Access}]", (await Assert.ThrowsAsync<InvalidOperationException>(() => twice.StartAsync(Ct))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Area_registra_o_servico_com_os_padroes_do_codigo_e_recusa_configuracao_invalida()
    {
        ServiceProvider Build(params (string Key, string? Value)[] settings) => new ServiceCollection().AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, s.Value))).Build())
            .AddSingleton<IJobProcessor>(_processor).AddSingleton<TimeProvider>(_time).AddFila().BuildServiceProvider();
        static (bool, TimeSpan, int) Values(ServiceProvider services)
        {
            var options = services.GetRequiredService<IOptions<FilaOptions>>().Value;
            return (options.Worker.Enabled, options.PollInterval, options.RetentionDays);
        }

        using var defaults = Build();
        using var configured = Build(("Fila:Worker:Enabled", "false"), ("Fila:PollInterval", "00:00:01"), ("Fila:RetentionDays", "7"));
        using var invalid = Build(("Fila:PollInterval", "00:00:00"));

        Assert.Equal((true, Poll, 30), Values(defaults));
        Assert.IsType<JobWorker>(Assert.Single(defaults.GetServices<IHostedService>()));
        Assert.Equal((false, TimeSpan.FromSeconds(1), 7), Values(configured));
        Assert.Throws<OptionsValidationException>(() => Values(invalid));
    }
}
