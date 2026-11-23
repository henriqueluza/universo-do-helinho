using Helinho.Application.Ai;
using Helinho.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Issue #227: opções de IA validadas na subida e o gerador que a porta resolve (ADR-0004 §14). Nenhum teste chama provedor.</summary>
[Collection(PostgresCollection.Name)]
public class AiProviderTests(PostgresFixture postgres)
{
    private const string ApiKey = "chave-sintetica-de-teste";
    private const string Model = "modelo-sintetico";

    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(formatter(state, exception) + exception);
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Configuração completa do provedor real, com <paramref name="overrides"/> por cima (valor vazio simula a variável ausente).</summary>
    private WebApplicationFactory<Program> CreateFactory(
        string environment, string provider, LogCollector? logs = null, params (string Key, string Value)[] overrides) =>
        new HelinhoWebFactory(postgres, configure: s => s.AddLogging(b => b.AddProvider(logs ?? new LogCollector())))
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                (string Key, string Value)[] settings = [("Ai:Provider", provider), ("Ai:ApiKey", ApiKey), ("Ai:Model", Model), .. overrides];
                foreach (var (key, value) in settings)
                {
                    b.UseSetting(key, value);
                }
            });

    private static (Type Generator, AiOptions Options) Resolve(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IQuestionDraftGenerator>().GetType(),
            scope.ServiceProvider.GetRequiredService<IOptionsMonitor<AiOptions>>().CurrentValue); // a instância que a subida validou
    }

    [Fact]
    public async Task Sem_a_variavel_vazia_ou_Off_a_IA_fica_desligada_e_a_porta_resolve_o_gerador_que_recusa()
    {
        await using var absent = new HelinhoWebFactory(postgres).WithWebHostBuilder(b => b.UseEnvironment(Environments.Production));
        await using var blank = CreateFactory(Environments.Production, "", null, ("Ai:ApiKey", ""), ("Ai:TimeoutSeconds", ""));
        await using var off = CreateFactory(Environments.Production, "off", null, ("Ai:Model", ""));

        foreach (var factory in new[] { absent, blank, off })
        {
            var (generator, options) = Resolve(factory);
            Assert.Equal(typeof(DisabledQuestionDraftGenerator), generator);
            Assert.False(options.IsEnabled);
            Assert.Equal(90, options.TimeoutSeconds);
        }
    }

    [Fact]
    public async Task Em_Production_o_provedor_configurado_e_as_cotas_chegam_as_opcoes()
    {
        await using var factory = CreateFactory(Environments.Production, AiOptions.Anthropic, null,
            ("Ai:TimeoutSeconds", "30"), ("Ai:DailyQuotaPerAdmin", "7"), ("Ai:DailyGlobalLimit", "9"));

        var (generator, options) = Resolve(factory);

        Assert.True(options.UsesAnthropic);
        Assert.Equal((30, 7, 9), (options.TimeoutSeconds, options.DailyQuotaPerAdmin, options.DailyGlobalLimit));
        // O adaptador do provedor ainda não existe neste build: a porta falha fechada.
        Assert.Equal(typeof(DisabledQuestionDraftGenerator), generator);
    }

    [Theory]
    [InlineData("Anthropic", "Ai:ApiKey", "")]
    [InlineData("Anthropic", "Ai:Model", "")]
    [InlineData("OpenAi", "Ai:Provider", "OpenAi")]
    [InlineData("Fake", "Ai:TimeoutSeconds", "noventa")]
    [InlineData("Off", "Ai:DailyQuotaPerAdmin", "-3")]
    public async Task Configuracao_invalida_derruba_a_subida_nomeando_a_variavel_sem_o_valor(string provider, string key, string value)
    {
        await using var factory = CreateFactory(Environments.Production, provider, null, (key, value));

        var failure = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.StartsWith(key, Assert.Single(failure.Failures), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, failure.Message, StringComparison.Ordinal);
        Assert.True(value.Length == 0 || !failure.Message.Contains(value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Em_Development_o_provedor_real_vira_Fake_com_aviso_e_provedor_desconhecido_ainda_falha()
    {
        var logs = new LogCollector();
        await using var real = CreateFactory(Environments.Development, AiOptions.Anthropic, logs, ("Ai:ApiKey", ""));
        await using var unknown = CreateFactory(Environments.Development, "OpenAi");

        var (_, options) = Resolve(real);

        Assert.True(options.UsesFake);
        Assert.Single(logs.Lines, line => line.Contains("Ai:Provider=Anthropic ignorado", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ApiKey, StringComparison.Ordinal));
        Assert.Throws<OptionsValidationException>(() => unknown.Services);
    }
}
