using System.Net;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>
/// Issue #265: rota do webhook da Brevo (ADR-0003 §12, D18) com o segredo configurado. Dados sintéticos; nenhuma chamada
/// à Brevo. A gravação em si é coberta na #277. As chamadas sem IP próprio dividem a mesma cota de 60 por minuto: a
/// classe inteira faz bem menos que isso.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BrevoWebhookTests(PostgresFixture postgres, BrevoWebhookTests.Host shared) : IClassFixture<BrevoWebhookTests.Host>
{
    /// <summary>Gerado a cada execução, com os 32 caracteres mínimos: nenhum segredo fica escrito no repositório.</summary>
    private static readonly string Secret = $"{Guid.NewGuid():N}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um host para a classe inteira (#215), com todo o log capturado.</summary>
    public sealed class Host : IAsyncDisposable, ILoggerProvider, ILogger
    {
        private WebApplicationFactory<Program>? _factory;

        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();

        internal async Task<WebApplicationFactory<Program>> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres, configure: s =>
                        s.AddLogging(b => b.AddProvider(this).AddFilter<Host>(null, LogLevel.Trace)))
                    .WithWebHostBuilder(b => b.UseSetting("Brevo:WebhookSecret", Secret));
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                _factory = factory;
            }

            return _factory;
        }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception) + exception);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => _factory?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>Corpo sem <c>Content-Length</c>, como numa requisição em pedaços.</summary>
    private sealed class SemTamanho(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length) => (length = 0) != 0;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>Corpo como o da Brevo, com os campos que a rota não pode gravar nem logar.</summary>
    private static StringContent Evento(string nome, string messageId) => Json(
        $$"""{"event":"{{nome}}","message-id":"{{messageId}}","email":"responsavel@example.com","subject":"Assunto sintético","reason":"motivo-sintetico"}""");

    /// <summary>Trabalho de e-mail já enviado; o <c>ExternalId</c>, que é do trabalhador gravar, vai direto no banco.</summary>
    private async Task<(Guid Job, string MessageId)> TrabalhoEnviadoAsync()
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new QueueJob(new JobRequest("email.student-access", Guid.NewGuid(), new JobPayload(), TimeSpan.FromHours(72)), DateTimeOffset.UtcNow);
        var messageId = $"{Guid.NewGuid():N}@relay.example.org";
        db.QueueJobs.Add(job);
        await db.SaveChangesAsync(Ct);
        await db.QueueJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ExternalId, messageId), Ct);
        return (job.Id, messageId);
    }

    private async Task<string?> RetornoAsync(Guid jobId)
    {
        await using var scope = (await shared.GetAsync(postgres)).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().QueueJobs.Where(j => j.Id == jobId).Select(j => j.Feedback).SingleAsync(Ct);
    }

    /// <summary>Chama a rota e confere o que vale para toda resposta: o status, nenhum corpo e nenhum cookie.</summary>
    private async Task ChamarAsync(HttpStatusCode esperado, HttpContent corpo, string? segredo)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BrevoWebhookArea.Route) { Content = corpo };
        if (segredo is not null)
        {
            request.Headers.Add(BrevoWebhookArea.SecretHeader, segredo);
        }

        using var response = await (await shared.GetAsync(postgres)).CreateClient().SendAsync(request, Ct);

        Assert.Equal(esperado, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(EmailFeedback.HardBounce)]
    [InlineData(EmailFeedback.InvalidEmail)]
    [InlineData(EmailFeedback.Blocked)]
    [InlineData(EmailFeedback.Spam)]
    public async Task Cada_um_dos_quatro_eventos_grava_o_retorno_so_naquele_trabalho_e_repetido_responde_200(string evento)
    {
        var (job, messageId) = await TrabalhoEnviadoAsync();
        var (outro, _) = await TrabalhoEnviadoAsync();

        await ChamarAsync(HttpStatusCode.OK, Evento(evento, $"<{messageId}>"), Secret);
        await ChamarAsync(HttpStatusCode.OK, Evento(evento, messageId), Secret);

        Assert.Equal(evento, await RetornoAsync(job));
        Assert.Null(await RetornoAsync(outro));
    }

    [Theory]
    [InlineData("""{"event":"delivered","message-id":"ID"}""")]
    [InlineData("""{"event":"soft_bounce","message-id":"ID"}""")]
    [InlineData("""{"event":"hard_bounce","message-id":"desconhecido-ID"}""")]
    [InlineData("""{"event":"hard_bounce","message-id":7}""")]
    [InlineData("""{"event":["hard_bounce"],"message-id":"ID"}""")]
    [InlineData("""[{"event":"hard_bounce","message-id":"ID"}]""")]
    [InlineData("{}")]
    public async Task Evento_fora_da_lista_corpo_de_outro_formato_e_message_id_desconhecido_respondem_200_sem_efeito(string corpo)
    {
        var (job, messageId) = await TrabalhoEnviadoAsync();

        await ChamarAsync(HttpStatusCode.OK, Json(corpo.Replace("ID", messageId, StringComparison.Ordinal)), Secret);

        Assert.Null(await RetornoAsync(job));
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("outro")]
    [InlineData("sem o último caractere")]
    [InlineData("com um caractere a mais")]
    public async Task Segredo_ausente_ou_errado_responde_401_sem_corpo_e_nada_e_gravado(string caso)
    {
        var segredo = caso switch
        {
            "ausente" => null,
            "outro" => $"{Guid.NewGuid():N}",
            "sem o último caractere" => Secret[..^1],
            _ => Secret + "0",
        };
        var (job, messageId) = await TrabalhoEnviadoAsync();

        await ChamarAsync(HttpStatusCode.Unauthorized, Evento(EmailFeedback.HardBounce, messageId), segredo);

        Assert.Null(await RetornoAsync(job));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Corpo_acima_de_64_KB_responde_413_com_ou_sem_Content_Length_e_no_limite_exato_passa(bool comTamanho)
    {
        var (job, messageId) = await TrabalhoEnviadoAsync();
        HttpContent Corpo(int bytes)
        {
            var conteudo = Encoding.UTF8.GetBytes($$"""{"event":"blocked","message-id":"{{messageId}}"}""".PadRight(bytes));
            return comTamanho ? new ByteArrayContent(conteudo) : new SemTamanho(conteudo);
        }

        await ChamarAsync(HttpStatusCode.RequestEntityTooLarge, Corpo(BrevoWebhookArea.MaxBodyBytes + 1), Secret);
        Assert.Null(await RetornoAsync(job));

        await ChamarAsync(HttpStatusCode.OK, Corpo(BrevoWebhookArea.MaxBodyBytes), Secret);
        Assert.Equal(EmailFeedback.Blocked, await RetornoAsync(job));
    }

    [Theory]
    [InlineData("")]
    [InlineData("event=hard_bounce&message-id=ID")]
    [InlineData("""{"event":"hard_bounce","message-id":"ID" """)]
    public async Task Corpo_que_nao_e_JSON_responde_400_sem_detalhe_e_nada_e_gravado(string corpo)
    {
        var (job, messageId) = await TrabalhoEnviadoAsync();

        await ChamarAsync(HttpStatusCode.BadRequest, Json(corpo.Replace("ID", messageId, StringComparison.Ordinal)), Secret);

        Assert.Null(await RetornoAsync(job));
    }

    [Fact]
    public async Task O_log_traz_so_o_tipo_do_evento_e_o_id_do_trabalho()
    {
        var (job, messageId) = await TrabalhoEnviadoAsync();
        var segredoErrado = $"{Guid.NewGuid():N}";

        await ChamarAsync(HttpStatusCode.OK, Evento(EmailFeedback.InvalidEmail, messageId), Secret);
        await ChamarAsync(HttpStatusCode.Unauthorized, Evento(EmailFeedback.InvalidEmail, messageId), segredoErrado);

        var log = string.Join('\n', shared.Lines);
        Assert.Contains($"retorno invalid_email gravado no trabalho {job}", log, StringComparison.Ordinal);
        Assert.Contains("segredo ausente ou incorreto", log, StringComparison.Ordinal);
        string[] proibido = [Secret, segredoErrado, messageId, "responsavel@example.com", "Assunto sintético", "motivo-sintetico"];
        Assert.All(proibido, valor => Assert.DoesNotContain(valor, log, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_61a_chamada_do_mesmo_IP_no_minuto_responde_429_antes_de_conferir_o_segredo()
    {
        var server = (await shared.GetAsync(postgres)).Server;
        var ip = IPAddress.Parse("203.0.113.61"); // faixa de documentação; cota própria, separada da dos outros testes
        async Task<int> ChamarSemSegredoAsync() => (await server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = BrevoWebhookArea.Route;
            context.Connection.RemoteIpAddress = ip;
        }, Ct)).Response.StatusCode;

        for (var chamada = 1; chamada <= BrevoWebhookArea.CallsPerMinute; chamada++)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, await ChamarSemSegredoAsync());
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await ChamarSemSegredoAsync());
    }

    [Fact]
    public async Task A_rota_fica_fora_da_descricao_OpenAPI_do_jogo()
    {
        var openApi = await (await shared.GetAsync(postgres)).CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.DoesNotContain("webhook", openApi, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Issue #265: o webhook desligado e a validação do segredo na subida (ADR-0003 §7 e §12).</summary>
[Collection(PostgresCollection.Name)]
public class BrevoWebhookConfiguracaoTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Sem_segredo_configurado_a_rota_nao_existe()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var content = new StringContent("{}");

        using var response = await factory.CreateClient().PostAsync(new Uri(BrevoWebhookArea.Route, UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Sem host: as opções da área e o validador que o host roda ao subir (<c>ValidateOnStart</c>).</summary>
    [Theory]
    [InlineData(0, "", true)]
    [InlineData(BrevoWebhookOptions.MinSecretLength, "", true)]
    [InlineData(BrevoWebhookOptions.MinSecretLength - 1, "", false)]
    [InlineData(BrevoWebhookOptions.MinSecretLength, " ", false)]
    [InlineData(BrevoWebhookOptions.MinSecretLength, "\n", false)]
    public void Segredo_preenchido_com_menos_de_32_caracteres_ou_com_espaco_derruba_a_subida_sem_mostrar_o_valor(int tamanho, string sufixo, bool valido)
    {
        var segredo = new string('s', tamanho) + sufixo;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Brevo:WebhookSecret"] = segredo }).Build());
        services.AddBrevoWebhook();
        using var provider = services.BuildServiceProvider();
        var validador = provider.GetRequiredService<IStartupValidator>();

        if (valido)
        {
            validador.Validate();
            return;
        }

        var falha = Assert.Throws<OptionsValidationException>(validador.Validate);
        Assert.StartsWith("Brevo:WebhookSecret", Assert.Single(falha.Failures), StringComparison.Ordinal);
        Assert.DoesNotContain(segredo, falha.Message, StringComparison.Ordinal);
    }
}
