using Helinho.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Composition;

/// <summary>Seção <c>Fila</c> (ADR-0003 §6.4), com padrão no código. Tentativas, lote e posse são da política de cada tratador.</summary>
public sealed class FilaOptions
{
    public const string Section = "Fila";

    public FilaWorkerOptions Worker { get; set; } = new();

    /// <summary>Espera até a próxima consulta quando a raia não encheu o lote.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Linhas em estado final são apagadas tantos dias depois de <c>FinishedAt</c> (§5.6).</summary>
    public int RetentionDays { get; set; } = 30;
}

public sealed class FilaWorkerOptions
{
    /// <summary><c>Fila__Worker__Enabled=false</c> desliga os laços (fábrica dos testes de integração); a política ainda é conferida na subida.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Área Fila: o serviço hospedado que chama <see cref="IJobProcessor"/> em laço. O processamento mora na infraestrutura.</summary>
public static class FilaArea
{
    public static IServiceCollection AddFila(this IServiceCollection services)
    {
        services.AddOptions<FilaOptions>()
            .BindConfiguration(FilaOptions.Section)
            .Validate(o => o.PollInterval > TimeSpan.Zero, "Fila:PollInterval precisa ser positivo.")
            .Validate(o => o.RetentionDays >= 1, "Fila:RetentionDays precisa ser de pelo menos 1 dia.")
            .ValidateOnStart();
        services.AddHostedService<JobWorker>();
        return services;
    }
}

/// <summary>
/// Trabalhador da fila (§6.4): um laço por raia (registro de tratador) e um de limpeza diária, no mesmo serviço.
/// Os comandos <c>migrate</c> e <c>seed</c> saem antes de <c>app.RunAsync()</c>, então ele não sobe neles.
/// </summary>
public sealed partial class JobWorker(
    IServiceScopeFactory scopes, IJobProcessor processor, IOptions<FilaOptions> options, TimeProvider time, ILogger<JobWorker> logger)
    : BackgroundService
{
    private const int PurgeBatchSize = 500;
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromDays(1);
    private readonly CancellationTokenSource _abort = new();
    private IReadOnlyList<JobLane> _lanes = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            _lanes = JobLane.From(scope.ServiceProvider.GetServices<IJobHandler>()); // política inválida: a aplicação não sobe
        }

        await base.StartAsync(cancellationToken);
    }

    /// <summary>O sinal de parada não cancela o item em curso; o fim da janela de parada do host, sim (§6.4).</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken); // volta quando os laços terminam ou quando a janela de parada acaba
        await _abort.CancelAsync(); // se a janela acabou, cancela o item em curso; se os laços já terminaram, não há o que cancelar
    }

    public override void Dispose()
    {
        _abort.Cancel(); // como o BackgroundService, só cancela: o host de teste chama StopAsync de novo depois do descarte
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var abort = _abort.Token;
        var settings = options.Value;
        Started(settings.Worker.Enabled, _lanes.Count);
        if (!settings.Worker.Enabled)
        {
            return Task.CompletedTask;
        }

        async Task<TimeSpan> PurgeAsync()
        {
            var deleted = await processor.PurgeFinishedAsync(TimeSpan.FromDays(settings.RetentionDays), PurgeBatchSize, stoppingToken);
            Purged(deleted);
            return PurgeInterval;
        }

        async Task<TimeSpan> PassAsync(JobLane lane)
        {
            var batch = await processor.ProcessBatchAsync(lane, stoppingToken, abort);
            return batch.PausedUntil is { } until ? until - time.GetUtcNow() // §6.3: pausa só esta raia
                : batch.Captured >= lane.Policy.BatchSize ? TimeSpan.Zero : settings.PollInterval; // lote cheio: sem espera
        }

        var lanes = _lanes.Select(lane => LoopAsync(string.Join(",", lane.Types), settings.PollInterval, () => PassAsync(lane), stoppingToken));
        return Task.WhenAll([LoopAsync("limpeza", PurgeInterval, PurgeAsync, stoppingToken), .. lanes]);
    }

    /// <summary>Cada volta devolve quanto esperar. Uma exceção não derruba o host nem os outros laços: só o tipo dela vai para o log.</summary>
    private async Task LoopAsync(string name, TimeSpan waitAfterError, Func<Task<TimeSpan>> pass, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            var wait = waitAfterError;
            try
            {
                wait = await pass();
            }
            catch (Exception exception)
            {
                if (stopping.IsCancellationRequested)
                {
                    return; // parada: o item interrompido reaparece pelo prazo de posse
                }

                LoopFailed(name, exception.GetType().Name);
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, stopping).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fila: trabalhador ligado = {Enabled}, {Lanes} raia(s).")]
    private partial void Started(bool enabled, int lanes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fila: limpeza apagou {Deleted} linha(s) em estado final.")]
    private partial void Purged(int deleted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fila: o laço {Loop} falhou com {ExceptionType}; segue depois da espera.")]
    private partial void LoopFailed(string loop, string exceptionType);
}
