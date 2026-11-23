using System.Collections.Concurrent;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ServiceProvider _services;
    private Func<Call, Task<JobResult>> _handle = _ => Task.FromResult(Sent);

    public JobProcessorTests(PostgresFixture postgres)
    {
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

    private sealed class FakeHandler(JobProcessorTests test, IServiceProvider scope, string[] types) : IJobHandler
    {
        public IReadOnlyCollection<string> Types => types;

        public JobPolicy Policy => Email;

        public Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
        {
            var call = NewCall(job, cancellationToken);
            test._calls.Enqueue(call);
            return test._handle(call);
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
        _handle = async call =>
        {
            Assert.Null(call.Db.Database.CurrentTransaction); // nenhuma transação aberta durante o tratador
            captured.Enqueue(await StateAsync(call.Job.Id)); // a captura já está gravada: outra conexão a enxerga
            return Sent;
        };

        var batches = new[] { await PassAsync(Lane with { Policy = Email with { BatchSize = 2 } }), await PassAsync(), await PassAsync() };

        Assert.Equal([2, 1, 0], batches.Select(b => b.Captured));
        Assert.Equal(ids[..3], _calls.Select(c => c.Job.Id));
        Assert.All(_calls, c => Assert.Equal((1, Invite), (c.Job.Attempts, c.Job.Type)));
        Assert.All(_calls, c => Assert.Contains(Invite, c.Handler.Types)); // roteado pelo Type para o registro certo
        Assert.All(captured, state => Assert.Equal("Processing|1||||12:02:00", state));
        Assert.Equal(3, _calls.Select(c => c.Db).Distinct().Count()); // escopo próprio por item
        var rows = await RowsAsync();
        Assert.StartsWith("Cancelled|0|", State(rows[ids[3]]), StringComparison.Ordinal); // o cancelado nunca é capturado
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
}
