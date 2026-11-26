using System.Collections.Concurrent;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>Processador da fila (ADR-0003 §6) contra o PostgreSQL, com tratador falso e relógio manual. Sem host web.</summary>
[Collection(PostgresCollection.Name)]
public sealed class JobProcessorTests : IAsyncLifetime
{
    private const string Invite = "email.admin-invite";
    private const string Follow = "email.password-changed"; // o que o tratador deixa no contexto, sem salvar
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobPolicy Email = new(8, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(60), 10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2));
    private static readonly JobLane Lane = new([Invite], Email);
    private static readonly JobResult Sent = new JobResult.Succeeded("msg-1");

    private readonly ManualTime _time = new(Now);
    private readonly Recorder _log = new();
    private readonly SaveCounter _saves = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    private readonly ConcurrentQueue<string> _abandoned = new();
    private readonly ServiceProvider _services;
    private readonly PostgresFixture _postgres;
    private Func<Call, Task<JobResult>> _handle = _ => Task.FromResult(Sent);
    private Func<Call, Task> _onAbandoned = call => call.Queue.EnqueueAsync(Request(Follow), call.Token);

    public JobProcessorTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = $"fila_{Guid.NewGuid():N}" }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("ConnectionStrings:Default", connection)]).Build();
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time).AddSingleton<ILogger<JobProcessor>>(_log)
            .AddDomainPersistence(configuration).AddInfrastructureServices() // a composição real registra o processador
            .ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(_saves))
            // Dois registros da mesma classe, com tipos diferentes: o roteamento é pelo Type.
            .AddScoped<IJobHandler>(sp => new FakeHandler(this, sp, [Invite, "email.password-reset"]))
            .AddScoped<IJobHandler>(sp => new FakeHandler(this, sp, ["ai.question-drafts"]))
            .BuildServiceProvider();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Call(JobContext Job, FakeHandler Handler, AppDbContext Db, IJobQueue Queue, CancellationToken Token);

    private sealed class FakeHandler(JobProcessorTests test, IServiceProvider scope, string[] types) : IJobHandler, IAbandonedJobHandler
    {
        public IReadOnlyCollection<string> Types => types;

        public JobPolicy Policy => Email;

        public Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
        {
            var call = NewCall(job, cancellationToken);
            test._calls.Enqueue(call);
            return test._handle(call);
        }

        public Task OnAbandonedAsync(JobContext job, string reason, CancellationToken cancellationToken)
        {
            test._abandoned.Enqueue(reason);
            return test._onAbandoned(NewCall(job, cancellationToken));
        }

        private Call NewCall(JobContext job, CancellationToken token) =>
            new(job, this, scope.GetRequiredService<AppDbContext>(), scope.GetRequiredService<IJobQueue>(), token);
    }

    private sealed class Recorder : ILogger<JobProcessor>
    {
        public ConcurrentQueue<(LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue((logLevel, formatter(state, exception)));
    }

    /// <summary>Conta as gravações e, a pedido, faz a próxima falhar (como um commit que não chega ao banco).</summary>
    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        public bool FailNext { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count++;
            (FailNext, var fail) = (false, FailNext);
            return fail ? throw new NpgsqlException("falha simulada") : ValueTask.FromResult(result);
        }
    }

    public async ValueTask InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(Ct);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    private static JobRequest Request(string type = Invite, double hours = 24) =>
        new(type, Guid.NewGuid(), new JobPayload().WithCode("reason", "resend"), TimeSpan.FromHours(hours));

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> use)
    {
        await using var scope = _services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Guid[]> EnqueueAsync(params JobRequest[] requests) => DbAsync(async db =>
    {
        var jobs = requests.Select((request, i) => new QueueJob(request, Now.AddSeconds(i - 60))).ToList(); // ordem de captura estável
        db.QueueJobs.AddRange(jobs);
        await db.SaveChangesAsync(Ct);
        return jobs.Select(j => j.Id).ToArray();
    });

    private Task<Dictionary<Guid, QueueJob>> RowsAsync() => DbAsync(db => db.QueueJobs.AsNoTracking().ToDictionaryAsync(j => j.Id, Ct));

    /// <summary>Estado, tentativas, código, id externo, término e próxima tentativa (que é também o prazo de posse).</summary>
    private async Task<string> StateAsync(Guid id) => State((await RowsAsync())[id]);

    private static string State(QueueJob j) =>
        $"{j.Status}|{j.Attempts}|{j.LastError}|{j.ExternalId}|{j.FinishedAt:HH:mm}|{j.NextAttemptAt:HH:mm:ss}";

    private async Task<int> FollowUpsAsync() => (await RowsAsync()).Values.Count(j => j.Type == Follow);

    private Task<int> SqlAsync(FormattableString sql) => DbAsync(db => db.Database.ExecuteSqlAsync(sql, Ct));

    private Task<JobBatchResult> PassAsync(JobLane? lane = null, CancellationTokenSource? stopping = null, CancellationTokenSource? abort = null, IJobProcessor? processor = null) =>
        (processor ?? _services.GetRequiredService<IJobProcessor>()).ProcessBatchAsync(lane ?? Lane, stopping?.Token ?? CancellationToken.None, abort?.Token ?? CancellationToken.None);

    [Fact]
    public async Task Captura_filtra_por_tipo_respeita_o_lote_conta_a_tentativa_e_empurra_a_posse()
    {
        var ids = await EnqueueAsync(Request(), Request(), Request(), Request(), Request("email.password-reset"), Request("ai.question-drafts"));
        await SqlAsync($"""UPDATE "QueueJobs" SET "Status" = 'Cancelled' WHERE "Id" = {ids[3]}""");
        var captured = new ConcurrentQueue<string>();
        var transactions = new ConcurrentQueue<bool>();
        _handle = async call =>
        {
            transactions.Enqueue(call.Db.Database.CurrentTransaction is not null);
            captured.Enqueue(await StateAsync(call.Job.Id)); // a captura já está gravada: outra conexão a enxerga
            return Sent;
        };

        var batches = new[] { await PassAsync(Lane with { Policy = Email with { BatchSize = 2 } }), await PassAsync(), await PassAsync() };

        Assert.Equal([2, 1, 0], batches.Select(b => b.Captured));
        Assert.Equal(ids[..3], _calls.Select(c => c.Job.Id));
        Assert.All(_calls, c => Assert.Equal((1, Invite), (c.Job.Attempts, c.Job.Type)));
        Assert.All(_calls, c => Assert.Contains(Invite, c.Handler.Types)); // roteado pelo Type para o registro certo
        Assert.Equal([false, false, false], transactions); // nenhuma transação aberta durante o tratador
        Assert.All(captured, state => Assert.Equal("Processing|1||||12:02:00", state));
        Assert.Equal(3, _calls.Select(c => c.Db).Distinct().Count()); // escopo próprio por item
        var rows = await RowsAsync();
        Assert.StartsWith("Cancelled|0|", State(rows[ids[3]]), StringComparison.Ordinal); // o cancelado nunca é capturado...
        Assert.Empty(_abandoned); // ...nem passa pelo gancho
        Assert.All(ids[4..], id => Assert.StartsWith("Pending|0|", State(rows[id]), StringComparison.Ordinal)); // tipos de outra raia
    }

    [Fact]
    public async Task Dois_processadores_concorrentes_nunca_pegam_a_mesma_linha()
    {
        var ids = await EnqueueAsync(Request(), Request(), Request(), Request());
        await using var scope = _services.CreateAsyncScope();
        var other = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // outro processo, no meio da captura dele
        await using (await other.Database.BeginTransactionAsync(Ct))
        {
            var locked = ids[..2];
            await other.Database.ExecuteSqlAsync($"""SELECT 1 FROM "QueueJobs" WHERE "Id" = ANY({locked}) FOR UPDATE""", Ct);
            Assert.Equal(2, (await PassAsync()).Captured); // SKIP LOCKED: não espera pelas travadas nem as pega
            Assert.Equal(ids[2..], _calls.Select(c => c.Job.Id));
        }

        var second = ActivatorUtilities.CreateInstance<JobProcessor>(_services);
        var one = Lane with { Policy = Email with { BatchSize = 1 } };
        var batches = await Task.WhenAll(PassAsync(one), PassAsync(one, processor: second));

        Assert.Equal([1, 1], batches.Select(b => b.Captured));
        Assert.Equal(ids.Order(), _calls.Select(c => c.Job.Id).Order()); // cada trabalho foi entregue uma vez só
        Assert.All((await RowsAsync()).Values, j => Assert.StartsWith("Succeeded|1|", State(j), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trabalho_de_processo_que_morreu_reaparece_depois_do_prazo_de_posse_com_a_tentativa_contada()
    {
        var id = (await EnqueueAsync(Request()))[0];
        using var abort = new CancellationTokenSource();
        _handle = async call =>
        {
            await abort.CancelAsync(); // a janela de parada do host acabou no meio do item
            return call.Token.IsCancellationRequested ? throw new OperationCanceledException(call.Token) : Sent;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PassAsync(abort: abort));
        Assert.Equal("Processing|1||||12:02:00", await StateAsync(id)); // nada foi gravado

        _handle = _ => Task.FromResult(Sent);
        _time.Advance(TimeSpan.FromSeconds(119));
        Assert.Equal(0, (await PassAsync()).Captured);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await PassAsync()).Captured);
        Assert.Equal([1, 2], _calls.Select(c => c.Job.Attempts));
    }

    [Fact]
    public async Task Sinal_de_parada_deixa_o_item_em_curso_terminar_e_nao_comeca_o_seguinte()
    {
        var ids = await EnqueueAsync(Request(), Request());
        using var stopping = new CancellationTokenSource();
        bool? cancelled = null;
        _handle = async call =>
        {
            await stopping.CancelAsync();
            cancelled = call.Token.IsCancellationRequested;
            return Sent;
        };

        await PassAsync(stopping: stopping);
        Assert.False(cancelled); // o sinal de parada, sozinho, não cancela o item
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PassAsync(stopping: stopping)); // nenhum lote novo

        Assert.Equal("Succeeded|1||msg-1|12:00|12:02:00", await StateAsync(ids[0]));
        Assert.Equal("Processing|1||||12:02:00", await StateAsync(ids[1])); // reaparece pelo prazo de posse
    }

    [Fact]
    public async Task Tempo_maximo_do_item_cancela_o_token_e_a_excecao_do_tratador_vira_repetir_sem_salvar_o_contexto()
    {
        var id = (await EnqueueAsync(Request()))[0];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handle = async call =>
        {
            await call.Queue.EnqueueAsync(Request(Follow), call.Token);
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, call.Token); // só o token do item acorda o tratador
            return Sent;
        };

        var pass = PassAsync();
        await entered.Task;
        _time.Advance(Email.ItemTimeout);
        await pass;

        var row = (await RowsAsync())[id];
        Assert.StartsWith("Pending|1|handler_exception|", State(row), StringComparison.Ordinal);
        Assert.InRange(row.NextAttemptAt, _time.GetUtcNow().AddSeconds(48), _time.GetUtcNow().AddSeconds(72)); // 1 minuto, ±20%
        Assert.Equal(0, await FollowUpsAsync());
        Assert.Contains(_log.Lines, l => l.Text.EndsWith("CanceledException.", StringComparison.Ordinal)); // Task ou Operation
    }

    [Fact]
    public async Task Ultima_tentativa_que_estoura_o_tempo_maximo_da_ao_gancho_um_token_vivo_e_grava_o_que_ele_deixou_com_o_Failed()
    {
        var id = (await EnqueueAsync(Request()))[0];
        await SqlAsync($"""UPDATE "QueueJobs" SET "Attempts" = 7 WHERE "Id" = {id}"""); // a captura conta a oitava, a última
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handle = async call =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, call.Token);
            return Sent;
        };
        bool? cancelled = null;
        _onAbandoned = async call =>
        {
            cancelled = call.Token.IsCancellationRequested;
            call.Token.ThrowIfCancellationRequested(); // como qualquer I/O que recebe o token
            await call.Queue.EnqueueAsync(Request(Follow), call.Token);
        };

        var pass = PassAsync();
        await entered.Task;
        _time.Advance(Email.ItemTimeout);
        await pass;

        Assert.Equal(["max_attempts"], _abandoned);
        Assert.False(cancelled); // o token do gancho é próprio: o do tratador acabou de vencer
        Assert.Equal("Failed|8|max_attempts||12:00|12:02:00", await StateAsync(id));
        Assert.Equal(1, await FollowUpsAsync()); // o que o gancho deixou sai na mesma gravação do Failed
    }

    [Fact]
    public async Task Resultado_do_tratador_e_conclusao_saem_na_mesma_gravacao_e_o_log_nao_leva_conteudo()
    {
        var ids = await EnqueueAsync(Request(), Request(), Request());
        JobResult[] results = [Sent, new JobResult.Fail("subject_missing"), new JobResult.Retry("http_429", Now.AddMinutes(30))];
        _handle = async call =>
        {
            await call.Queue.EnqueueAsync(Request(Follow), call.Token); // fica no contexto: quem salva é o processador
            return results[Array.IndexOf(ids, call.Job.Id)];
        };
        var saves = _saves.Count;

        await PassAsync();

        Assert.Equal(3, _saves.Count - saves); // uma gravação por item
        Assert.Equal(3, await FollowUpsAsync()); // vale para qualquer resultado
        Assert.Equal("Succeeded|1||msg-1|12:00|12:02:00", await StateAsync(ids[0]));
        Assert.Equal("Failed|1|subject_missing||12:00|12:02:00", await StateAsync(ids[1]));
        Assert.Equal("Pending|1|http_429|||12:30:00", await StateAsync(ids[2])); // NotBefore do tratador
        Assert.Equal([LogLevel.Information, LogLevel.Warning, LogLevel.Information], _log.Lines.Select(l => l.Level));
        Assert.Equal($"Fila: trabalho {ids[1]} ({Invite}), tentativa 1: failed, código subject_missing, 0 ms.", _log.Lines.ElementAt(1).Text);
        // Só id, tipo, tentativa, classe do resultado, código e duração: sem payload, sujeito ou texto livre.
        Assert.All(_log.Lines, l => Assert.Matches(@"^Fila: trabalho [0-9a-f-]{36} \([a-z.-]+\), tentativa \d+: [a-z_]+, código [a-z0-9_-]+, \d+ ms\.$", l.Text));
    }

    [Fact]
    public async Task Guarda_de_posse_recusa_a_gravacao_inteira_de_quem_perdeu_o_trabalho()
    {
        var id = (await EnqueueAsync(Request()))[0];
        _handle = async call =>
        {
            await call.Queue.EnqueueAsync(Request(Follow), call.Token);
            await SqlAsync($"""UPDATE "QueueJobs" SET "Attempts" = "Attempts" + 1 WHERE "Id" = {id}"""); // outro processo pegou
            return Sent;
        };

        await PassAsync();

        Assert.Equal("Processing|2||||12:02:00", await StateAsync(id)); // nem a conclusão...
        Assert.Equal(0, await FollowUpsAsync()); // ...nem o resultado
        Assert.Equal(LogLevel.Warning, Assert.Single(_log.Lines, l => l.Text.Contains("lease_lost", StringComparison.Ordinal)).Level);
    }

    [Fact]
    public async Task Suspender_devolve_a_tentativa_pausa_a_raia_e_devolve_o_resto_do_lote_sem_chamar_o_tratador()
    {
        await EnqueueAsync(Request(), Request());
        _handle = _ => Task.FromResult<JobResult>(new JobResult.Suspend("http_402", TimeSpan.FromMinutes(15)));

        var suspended = await PassAsync();
        var idle = await PassAsync();

        Assert.Equal(new JobBatchResult(2, Now.AddMinutes(15)), suspended);
        Assert.Equal(new JobBatchResult(0, null), idle);
        Assert.Single(_calls); // o segundo item foi devolvido sem nova chamada ao provedor
        Assert.All((await RowsAsync()).Values, j => Assert.Equal("Pending|0|http_402|||12:15:00", State(j)));
        Assert.All(_log.Lines, l => Assert.Equal(LogLevel.Error, l.Level)); // é o gatilho de alerta

        _handle = _ => Task.FromResult(Sent);
        _time.Advance(TimeSpan.FromMinutes(15));
        await PassAsync();
        Assert.Equal([1, 1, 1], _calls.Select(c => c.Job.Attempts)); // a captura seguinte entrega o mesmo número de tentativa
    }

    [Fact]
    public async Task Posse_vencida_nao_grava_nada_nem_depois_de_uma_suspensao_gravada_por_quem_pegou_depois()
    {
        var id = (await EnqueueAsync(Request()))[0];
        _handle = async _ =>
        {
            if (_calls.Count == 2)
            {
                return new JobResult.Suspend("http_401", TimeSpan.FromMinutes(15)); // quem pegou depois, com a tentativa 2
            }

            _time.Advance(Email.Lease); // a posse do primeiro venceu no meio do tratador
            await PassAsync();
            Assert.Equal("Pending|1|http_401|||12:17:00", await StateAsync(id)); // o mesmo número do primeiro, mas pendente
            return Sent;
        };

        await PassAsync();

        Assert.Equal([1, 2], _calls.Select(c => c.Job.Attempts));
        Assert.Equal("Pending|1|http_401|||12:17:00", await StateAsync(id));
        Assert.Contains(_log.Lines, l => l.Text.Contains("lease_lost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falha_na_gravacao_final_nao_e_repetida_e_o_item_reaparece_pelo_prazo_de_posse()
    {
        var id = (await EnqueueAsync(Request()))[0];
        _handle = _ =>
        {
            _saves.FailNext = true;
            return Task.FromResult(Sent);
        };
        var saves = _saves.Count;

        await PassAsync();

        Assert.Equal(1, _saves.Count - saves); // uma tentativa de gravar, e só
        Assert.Equal("Processing|1||||12:02:00", await StateAsync(id));
        Assert.Contains(_log.Lines, l => l.Text.Contains("write_failed", StringComparison.Ordinal));
        Assert.False(await DbAsync(db => Task.FromResult(db.Database.CreateExecutionStrategy().RetriesOnFailure))); // sem EnableRetryOnFailure

        _handle = _ => Task.FromResult(Sent);
        _time.Advance(Email.Lease);
        await PassAsync();
        Assert.Equal("Succeeded|2||msg-1|12:02|12:04:00", await StateAsync(id));
    }

    [Fact]
    public async Task Fila_encerra_o_vencido_e_o_que_passou_do_teto_e_grava_o_que_o_gancho_deixou_junto_com_o_Failed()
    {
        var ids = await EnqueueAsync(Request(hours: 1), Request(), Request());
        await SqlAsync($"""UPDATE "QueueJobs" SET "Attempts" = 8 WHERE "Id" = {ids[1]}"""); // o processo morreu na última tentativa
        await SqlAsync($"""UPDATE "QueueJobs" SET "Attempts" = 7 WHERE "Id" = {ids[2]}""");
        _handle = _ => Task.FromResult<JobResult>(new JobResult.Retry("http_500"));
        _time.Advance(TimeSpan.FromHours(1));

        await PassAsync();

        Assert.Equal(ids[2], Assert.Single(_calls).Job.Id); // só o que ainda tinha tentativa chegou ao tratador
        Assert.Equal(["expired", "max_attempts", "max_attempts"], _abandoned);
        Assert.Equal("Failed|1|expired||13:00|13:02:00", await StateAsync(ids[0]));
        Assert.Equal("Failed|9|max_attempts||13:00|13:02:00", await StateAsync(ids[1]));
        Assert.Equal("Failed|8|max_attempts||13:00|13:02:00", await StateAsync(ids[2]));
        Assert.Equal(3, await FollowUpsAsync()); // o gancho não salva: sai na mesma gravação do Failed
    }

    [Fact]
    public async Task Gancho_que_lanca_excecao_nao_impede_o_Failed_e_nada_do_contexto_dele_e_salvo()
    {
        var id = (await EnqueueAsync(Request(hours: 1)))[0];
        _onAbandoned = async call =>
        {
            await call.Queue.EnqueueAsync(Request(Follow), call.Token);
            throw new InvalidOperationException("mensagem sintética que não pode ir para o log");
        };
        _time.Advance(TimeSpan.FromHours(1));

        await PassAsync();

        Assert.Equal("Failed|1|expired||13:00|13:02:00", await StateAsync(id));
        Assert.Equal(0, await FollowUpsAsync());
        Assert.Contains(_log.Lines, l => l.Text.EndsWith("exceção InvalidOperationException.", StringComparison.Ordinal)); // o tipo, nunca a mensagem
        Assert.DoesNotContain(_log.Lines, l => l.Text.Contains("sintética", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Limpeza_apaga_em_lotes_so_o_que_terminou_ha_mais_tempo_que_a_retencao()
    {
        var ids = await EnqueueAsync(Request(), Request(), Request(), Request(), Request(), Request());
        (string Status, int DaysAgo)[] rows = [("Succeeded", 31), ("Failed", 31), ("Cancelled", 31), ("Succeeded", 29), ("Pending", 31), ("Processing", 31)];
        foreach (var (id, (status, daysAgo)) in ids.Zip(rows))
        {
            await SqlAsync($"""UPDATE "QueueJobs" SET "Status" = {status}, "FinishedAt" = {Now.AddDays(-daysAgo)} WHERE "Id" = {id}""");
        }

        var deleted = await _services.GetRequiredService<IJobProcessor>().PurgeFinishedAsync(TimeSpan.FromDays(30), batchSize: 2, Ct);

        Assert.Equal(3, deleted); // dois lotes: 2 e 1
        Assert.Equal(ids[3..].Order(), (await RowsAsync()).Keys.Order()); // o recente, o pendente e o em processamento ficam
    }

    [Fact]
    public async Task Host_de_teste_sobe_com_o_trabalhador_desligado_e_o_processador_real()
    {
        await using var factory = new HelinhoWebFactory(_postgres);
        var services = factory.Services; // sobe o host e, com ele, os serviços hospedados

        Assert.False(services.GetRequiredService<IOptions<FilaOptions>>().Value.Worker.Enabled); // §6.4: desligado na fábrica
        Assert.Contains(services.GetServices<IHostedService>(), service => service is JobWorker);
        Assert.IsType<JobProcessor>(services.GetRequiredService<IJobProcessor>());
    }
}
