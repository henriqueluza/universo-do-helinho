using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Microsoft.Extensions.Logging;

namespace Helinho.UnitTests;

/// <summary>Pedido de redefinição (ADR-0003 §11.1): o que entra na fila, o limite por conta e o piso de tempo, com relógio falso.</summary>
public class PedirRedefinicaoHandlerTests
{
    private const string Email = "marina.teste@example.com";
    private const string Digest = "0123456789abcdef0123456789abcdef";
    private static readonly Guid UserId = Guid.Parse("3f2b8c1e-5d47-4a9b-8e21-7c6d5b4a3f10");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly Fakes _fakes = new();
    private readonly PedirRedefinicaoHandler _handler;

    public PedirRedefinicaoHandlerTests() => _handler = new PedirRedefinicaoHandler(_fakes, _fakes, _fakes, _fakes, _fakes);

    [Fact]
    public async Task Conta_apta_enfileira_so_referencias_com_a_chave_do_carimbo_e_da_janela()
    {
        _fakes.Conta = new ContaParaRedefinicao(UserId, Digest);

        await _handler.HandleAsync(Email, Ct);

        var pedido = Assert.Single(_fakes.Enfileirados);
        Assert.Equal((EmailJobTypes.PasswordReset, UserId, """{"v":1}""", TimeSpan.FromMinutes(30)),
            (pedido.Type, pedido.SubjectId, pedido.Payload.ToJson(), pedido.ExpiresIn));
        Assert.Equal($"email.password-reset:{UserId:D}:{Digest}:{Now.ToUnixTimeSeconds() / 120}", pedido.IdempotencyKey);
        Assert.Equal(1, _fakes.Gravacoes);
        // O limite conta só o carimbo atual, na janela da validade do link.
        Assert.Equal(($"email.password-reset:{UserId:D}:{Digest}:", Now - LinkDeAcesso.ValidadeRedefinicao), _fakes.Contagem);
    }

    [Fact]
    public async Task Sem_conta_apta_nada_e_consultado_na_fila_nem_enfileirado()
    {
        await _handler.HandleAsync(Email, Ct);

        Assert.Empty(_fakes.Enfileirados);
        Assert.Equal((0, null), (_fakes.Gravacoes, _fakes.Contagem));
    }

    [Fact]
    public async Task No_limite_por_conta_nada_e_enfileirado_e_o_log_leva_so_o_id()
    {
        _fakes.Conta = new ContaParaRedefinicao(UserId, Digest);
        _fakes.Vivos = PedirRedefinicaoHandler.LimitePorConta;

        await _handler.HandleAsync(Email, Ct);

        Assert.Empty(_fakes.Enfileirados);
        var (nivel, linha) = Assert.Single(_fakes.Logs);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Contains(UserId.ToString(), linha, StringComparison.Ordinal);
        Assert.DoesNotContain("example", linha, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, true, 0)] // repetido na mesma janela: a fila recusa a chave e nada é gravado
    [InlineData(false, false, 1)] // corrida entre dois pedidos: o banco recusa a gravação do segundo
    public async Task Pedido_repetido_nao_lanca_e_responde_no_mesmo_piso(bool chaveJaUsada, bool gravacaoAceita, int gravacoes)
    {
        _fakes.Conta = new ContaParaRedefinicao(UserId, Digest);
        (_fakes.ChaveJaUsada, _fakes.GravacaoAceita) = (chaveJaUsada, gravacaoAceita);

        await _handler.HandleAsync(Email, Ct);

        Assert.Equal(gravacoes, _fakes.Gravacoes);
        Assert.Equal(PedirRedefinicaoHandler.Piso, _fakes.Decorrido);
        Assert.DoesNotContain(_fakes.Logs, l => l.Nivel >= LogLevel.Warning || l.Linha.Contains("example", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, 0, 250)]
    [InlineData(false, 40, 250)]
    [InlineData(true, 40, 250)]
    [InlineData(true, 180, 250)]
    [InlineData(true, 400, 400)] // já passou do piso: não espera mais
    public async Task Os_dois_caminhos_so_respondem_depois_do_piso_de_tempo(bool contaExiste, int custoMs, int respostaMs)
    {
        _fakes.Conta = contaExiste ? new ContaParaRedefinicao(UserId, Digest) : null;
        _fakes.CustoDaConsulta = TimeSpan.FromMilliseconds(custoMs);

        await _handler.HandleAsync(Email, Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(respostaMs), _fakes.Decorrido);
        Assert.Equal(contaExiste ? 1 : 0, _fakes.Enfileirados.Count);
    }

    /// <summary>Portas falsas e relógio falso: a espera do piso avança o tempo na hora, sem <c>Task.Delay</c> real.</summary>
    private sealed class Fakes : TimeProvider, IPasswordResetAccounts, IJobQueue, IClock, ILogger<PedirRedefinicaoHandler>
    {
        public ContaParaRedefinicao? Conta { get; set; }
        public int Vivos { get; set; }
        public bool ChaveJaUsada { get; set; }
        public bool GravacaoAceita { get; set; } = true;
        public TimeSpan CustoDaConsulta { get; set; }
        public TimeSpan Decorrido { get; private set; }
        public (string Prefixo, DateTimeOffset Desde)? Contagem { get; private set; }
        public int Gravacoes { get; private set; }
        public List<JobRequest> Enfileirados { get; } = [];
        public List<(LogLevel Nivel, string Linha)> Logs { get; } = [];

        public DateTimeOffset UtcNow => Now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Decorrido.Ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Decorrido += dueTime;
            callback(state);
            return new TimerNulo();
        }

        public Task<ContaParaRedefinicao?> FindSingleAsync(string email, CancellationToken cancellationToken)
        {
            Decorrido += CustoDaConsulta;
            return Task.FromResult(Conta);
        }

        public Task<int> CountLiveAsync(Guid userId, string keyPrefix, DateTimeOffset since, CancellationToken cancellationToken)
        {
            Contagem = (keyPrefix, since);
            return Task.FromResult(Vivos);
        }

        public Task<bool> TrySaveAsync(CancellationToken cancellationToken)
        {
            Gravacoes++;
            return Task.FromResult(GravacaoAceita);
        }

        public Task<Guid?> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
        {
            Enfileirados.Add(request);
            return Task.FromResult<Guid?>(ChaveJaUsada ? null : Guid.NewGuid());
        }

        public Task<int> CancelPendingAsync(string type, Guid subjectId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Logs.Add((logLevel, formatter(state, exception)));

        private sealed class TimerNulo : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
