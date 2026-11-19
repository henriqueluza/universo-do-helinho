using System.Net;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Helinho.Infrastructure.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Issue #204: escolha Fake/Brevo e validação das opções na subida (ADR-0003 §7). Nenhum teste envia e-mail.</summary>
[Collection(PostgresCollection.Name)]
public class EmailProviderTests(PostgresFixture postgres)
{
    private const string ApiKey = "chave-sintetica-de-teste";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Recebe tudo, de <c>Trace</c> para cima, inclusive o log de cabeçalhos do <c>HttpClient</c>.</summary>
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

    /// <summary>No lugar da rede: a Brevo nunca é chamada.</summary>
    private sealed class AcceptingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"messageId":"<1@relay.example.org>"}""") });
    }

    /// <summary>Configuração completa da Brevo, com <paramref name="overrides"/> por cima (valor vazio simula a variável ausente).</summary>
    private WebApplicationFactory<Program> CreateFactory(
        string environment, string provider, LogCollector? logs = null, params (string Key, string Value)[] overrides) =>
        new HelinhoWebFactory(postgres, configure: s =>
        {
            s.AddLogging(b => b.AddProvider(logs ?? new LogCollector()).AddFilter<LogCollector>(null, LogLevel.Trace));
            s.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(h => h.PrimaryHandler = new AcceptingHandler()));
        }).WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            (string Key, string Value)[] settings =
            [
                ("Email:Provider", provider), ("Brevo:ApiKey", ApiKey), ("Brevo:SenderEmail", "nao-responda@example.org"),
                ("Brevo:SenderName", "Helinho Teste"), ("App:PublicBaseUrl", "https://painel.example.org"), .. overrides,
            ];
            foreach (var (key, value) in settings)
            {
                b.UseSetting(key, value);
            }
        });

    private static Type SenderType(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IEmailSender>().GetType();
    }

    [Fact]
    public async Task Em_Production_o_padrao_e_Fake_e_Brevo_completo_usa_o_adaptador()
    {
        await using var byDefault = new HelinhoWebFactory(postgres).WithWebHostBuilder(b => b.UseEnvironment(Environments.Production));
        await using var blank = CreateFactory(Environments.Production, "", null, ("Brevo:ApiKey", ""));
        await using var brevo = CreateFactory(Environments.Production, EmailOptions.Brevo);

        Assert.Equal(typeof(FakeEmailSender), SenderType(byDefault));
        Assert.Equal(typeof(FakeEmailSender), SenderType(blank));
        Assert.Equal(typeof(BrevoEmailSender), SenderType(brevo));
    }

    [Theory]
    [InlineData("Brevo", "Brevo:ApiKey", "")]
    [InlineData("Brevo", "Brevo:ApiKey", "chave com espaco")]
    [InlineData("Brevo", "Brevo:SenderEmail", "")]
    [InlineData("Brevo", "Brevo:SenderEmail", "Nome <nao-responda@example.org>")]
    [InlineData("Brevo", "Brevo:SenderName", "")]
    [InlineData("Brevo", "App:PublicBaseUrl", "")]
    [InlineData("Brevo", "App:PublicBaseUrl", "http://painel.example.org")]
    [InlineData("Brevo", "App:PublicBaseUrl", "https://painel.example.org/caminho")]
    [InlineData("Sendgrid", "Email:Provider", "Sendgrid")]
    public async Task Configuracao_invalida_derruba_a_subida_nomeando_a_variavel_sem_o_valor(string provider, string key, string value)
    {
        await using var factory = CreateFactory(Environments.Production, provider, null, (key, value));

        var failure = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.StartsWith(key, Assert.Single(failure.Failures), StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, failure.Message, StringComparison.Ordinal);
        Assert.True(value.Length == 0 || !failure.Message.Contains(value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Em_Development_o_provedor_e_sempre_Fake_com_aviso_mas_provedor_desconhecido_ainda_falha()
    {
        var logs = new LogCollector();
        await using var brevo = CreateFactory(Environments.Development, EmailOptions.Brevo, logs, ("Brevo:ApiKey", ""));
        await using var unknown = CreateFactory(Environments.Development, "Sendgrid");

        // Resolvido duas vezes: o aviso sai uma vez só, na subida.
        Assert.Equal(typeof(FakeEmailSender), SenderType(brevo));
        Assert.Equal(typeof(FakeEmailSender), SenderType(brevo));
        Assert.Single(logs.Lines, line => line.Contains("Email:Provider=Brevo ignorado", StringComparison.Ordinal));
        Assert.Throws<OptionsValidationException>(() => unknown.Services);
    }

    [Fact]
    public async Task Com_tudo_em_Trace_o_cliente_registrado_envia_a_chave_e_nao_grava_chave_nem_mensagem_em_log()
    {
        var logs = new LogCollector();
        await using var factory = CreateFactory(Environments.Production, EmailOptions.Brevo, logs);
        await using var scope = factory.Services.CreateAsyncScope();
        var message = new EmailMessage("responsavel@example.com", "Assunto sintético", "<p>corpo-html-sintetico</p>", "corpo-texto-sintetico");

        var result = await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, Ct);

        Assert.Equal(new EmailSendResult(EmailSendStatus.Sent, "1@relay.example.org"), result);
        var all = string.Join('\n', logs.Lines);
        Assert.Contains("api-key: *", all, StringComparison.Ordinal); // o log de cabeçalhos foi capturado, com o valor mascarado
        string[] secrets = [ApiKey, message.Recipient, message.Subject, "corpo-html-sintetico", message.TextBody];
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, all, StringComparison.Ordinal));
    }
}
