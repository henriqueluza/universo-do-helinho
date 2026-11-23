using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>
/// Um host por classe para os testes da IA com o trabalhador real da fila (Issues #298 e #301): o gerador é o que a
/// aplicação registra (Fake ou o desligado, conforme <c>Ai:Provider</c>), só observado; a hora é uma só
/// (<see cref="ManualTime"/>, que o <c>IClock</c> também lê) e o processador é chamado direto, sem espera real.
/// </summary>
public sealed class AiHostDeTeste : IAsyncDisposable
{
    private HelinhoWebFactory? _factory;

    public ManualTime Time { get; } = new(DateTimeOffset.UtcNow);

    public GeneratorProbe Probe { get; } = new();

    public async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
    {
        if (_factory is null)
        {
            _factory = new HelinhoWebFactory(postgres, configure: services =>
            {
                services.AddSingleton<TimeProvider>(Time);
                services.Configure<AiOptions>(o => o.Provider = AiOptions.Fake);
                var port = services.Last(d => d.ServiceType == typeof(IQuestionDraftGenerator)).ImplementationFactory!;
                services.AddTransient<IQuestionDraftGenerator>(provider => new ObservedGenerator((IQuestionDraftGenerator)port(provider), Probe));
            });
            await _factory.EnsureDatabaseAsync();
        }

        return _factory;
    }

    /// <summary>Muda as opções vivas do host (as duas visões que a aplicação lê) e zera o que o observador guardou.</summary>
    public void Configure(string provider = AiOptions.Fake, FakeAiFailure failure = FakeAiFailure.None, int quota = 60, int global = 300, int timeout = 90)
    {
        var services = _factory!.Services;
        foreach (var options in new[] { services.GetRequiredService<IOptions<AiOptions>>().Value, services.GetRequiredService<IOptionsMonitor<AiOptions>>().CurrentValue })
        {
            (options.Provider, options.DailyQuotaPerAdmin, options.DailyGlobalLimit, options.TimeoutSeconds) = (provider, quota, global, timeout);
        }

        services.GetRequiredService<IOptions<FakeAiOptions>>().Value.Failure = failure;
        Probe.Reset();
    }

    /// <summary>As raias como o trabalhador as monta na subida: uma por tratador registrado.</summary>
    public async Task<IReadOnlyList<JobLane>> LanesAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return JobLane.From(scope.ServiceProvider.GetServices<IJobHandler>());
    }

    /// <summary>Uma passada do processador real na raia da IA, como o laço do trabalhador faria.</summary>
    public async Task<JobBatchResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var lane = (await LanesAsync()).Single(l => l.Types.Contains(RequestAiDrafts.JobType));
        return await _factory!.Services.GetRequiredService<IJobProcessor>().ProcessBatchAsync(lane, cancellationToken, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await (_factory?.DisposeAsync() ?? ValueTask.CompletedTask);

    /// <summary>O que chegou à porta: qual gerador a aplicação escolheu em cada chamada. <see cref="Script"/> troca a resposta.</summary>
    public sealed class GeneratorProbe
    {
        private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Type> Generators { get; } = [];

        /// <summary>Resposta roteirizada no lugar do gerador registrado (texto hostil, por exemplo). Nenhuma chamada real.</summary>
        public Func<QuestionDraftRequest, QuestionDraftBatch>? Script { get; set; }

        /// <summary>Conclui quando a porta é chamada: o teste só adianta a hora com o tratador já dentro do gerador.</summary>
        public Task Entered => _entered.Task;

        public void Reset()
        {
            Generators.Clear();
            Script = null;
            _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Enter(Type generator)
        {
            Generators.Add(generator);
            _entered.TrySetResult();
        }
    }

    private sealed class ObservedGenerator(IQuestionDraftGenerator inner, GeneratorProbe probe) : IQuestionDraftGenerator
    {
        public Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
        {
            probe.Enter(inner.GetType());
            return probe.Script is { } script ? Task.FromResult(script(request)) : inner.GenerateAsync(request, cancellationToken);
        }
    }
}
