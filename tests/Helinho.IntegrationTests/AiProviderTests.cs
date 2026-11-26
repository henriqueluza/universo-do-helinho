using System.Net;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Ai;
using Helinho.Infrastructure.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
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
        Assert.Equal(typeof(AnthropicQuestionDraftGenerator), generator);
    }

    /// <summary>Issue #250: a cadeia é a que a aplicação registra; nada é enviado, só o handler é criado.</summary>
    [Fact]
    public async Task O_cliente_da_Anthropic_nao_segue_redirecionamento_nem_tem_o_tempo_limite_padrao()
    {
        await using var factory = CreateFactory(Environments.Production, AiOptions.Anthropic);

        var handler = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(AnthropicQuestionDraftGenerator));
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.AllowAutoRedirect || sockets.UseCookies);
        Assert.Equal(Timeout.InfiniteTimeSpan, factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AnthropicQuestionDraftGenerator)).Timeout);
    }

    /// <summary>Issue #250: no lugar da rede, um handler que devolve uma resposta escrita à mão. A Anthropic nunca é chamada.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            const string Body = """
                {"id":"msg_sintetico","type":"message","role":"assistant","model":"modelo-sintetico-que-respondeu","stop_reason":"end_turn",
                 "content":[{"type":"text","text":"{\"questoes\":[{\"enunciado\":\"resposta-bruta-sintetica?\",\"a\":\"Um\",\"b\":\"Dois\",\"c\":\"Tres\",\"d\":\"Quatro\",\"correta\":\"a\",\"explicacao\":\"e\"}]}"}],
                 "usage":{"input_tokens":10,"output_tokens":20}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Com_tudo_em_Trace_o_cliente_registrado_envia_a_chave_e_nao_grava_chave_assunto_prompt_nem_resposta_em_log()
    {
        var logs = new LogCollector();
        using var network = new CannedHandler();
        await using var factory = new HelinhoWebFactory(postgres, configure: s =>
        {
            s.AddLogging(b => b.AddProvider(logs).AddFilter<LogCollector>(null, LogLevel.Trace));
            s.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = network));
        }).WithWebHostBuilder(b => b.UseEnvironment(Environments.Production)
            .UseSetting("Ai:Provider", AiOptions.Anthropic).UseSetting("Ai:ApiKey", ApiKey).UseSetting("Ai:Model", Model));
        var request = QuestionDraftRequest.Create(QuizType.Cultural, AgeBand.Age11To12, 1, "assunto-sintetico-secreto", ["enunciado-existente-sintetico"]);

        var batch = await factory.Services.GetRequiredService<IQuestionDraftGenerator>().GenerateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("resposta-bruta-sintetica?", Assert.Single(batch.Drafts).Statement);
        Assert.Equal($"Bearer {ApiKey}", network.Request?.Headers.Authorization?.ToString());
        var all = string.Join('\n', logs.Lines);
        Assert.Contains("Authorization: *", all, StringComparison.Ordinal); // o log de cabeçalhos foi capturado, com o valor mascarado
        string[] secrets = [ApiKey, "assunto-sintetico-secreto", "enunciado-existente-sintetico", "resposta-bruta-sintetica", "Você escreve questões"];
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, all, StringComparison.Ordinal));
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

        var (generator, options) = Resolve(real);

        Assert.True(options.UsesFake);
        Assert.Equal(typeof(FakeQuestionDraftGenerator), generator);
        Assert.Single(logs.Lines, line => line.Contains("Ai:Provider=Anthropic ignorado", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(ApiKey, StringComparison.Ordinal));
        Assert.Throws<OptionsValidationException>(() => unknown.Services);
    }

    /// <summary>Issue #232: com <c>Fake</c> a porta resolve o gerador fictício, que lê <c>Ai:Fake:*</c> da configuração.</summary>
    [Fact]
    public async Task Com_Fake_a_porta_gera_rascunhos_ficticios_e_a_falha_simulada_vem_da_configuracao()
    {
        await using var fake = CreateFactory(Environments.Production, "fake", null, ("Ai:ApiKey", ""), ("Ai:Model", ""));
        await using var refusing = CreateFactory(Environments.Production, AiOptions.Fake, null, ("Ai:Fake:Failure", "Refused"));
        var request = QuestionDraftRequest.Create(QuizType.Cultural, AgeBand.Age11To12, 4, null, []);
        var ct = TestContext.Current.CancellationToken;

        var batch = await fake.Services.GetRequiredService<IQuestionDraftGenerator>().GenerateAsync(request, ct);
        var failure = await Assert.ThrowsAsync<QuestionDraftGenerationException>(
            () => refusing.Services.GetRequiredService<IQuestionDraftGenerator>().GenerateAsync(request, ct));

        Assert.Equal((4, FakeQuestionDraftGenerator.Provider), (batch.Drafts.Count, batch.Usage.Provider));
        Assert.Equal(QuestionDraftFailure.Refused, failure.Reason);
        using var scope = fake.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RequestAiDrafts>()); // Issue #258: o caso de uso está na composição
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GetAiDraftJob>()); // Issue #275
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ReviewAiDrafts>()); // Issue #260
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SuggestSingleDraft>()); // Issue #261
        Assert.Contains(scope.ServiceProvider.GetServices<IJobHandler>(), handler => handler is ProcessAiDraftJob); // Issue #259: a raia da IA
    }
}
