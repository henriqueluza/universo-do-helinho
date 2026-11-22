using System.Net;
using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

/// <summary>Issue #204: o adaptador da Brevo contra um <see cref="HttpMessageHandler"/> falso. Nenhum teste sai para a rede.</summary>
public class BrevoEmailSenderTests
{
    private const string ApiKey = "chave-sintetica-de-teste";
    private const string ProviderEcho = "eco-do-provedor";
    private static readonly Guid JobId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly EmailMessage Message = new(
        "responsavel@example.com", "Assunto sintético", "<p>corpo-html-sintetico</p>", "corpo-texto-sintetico", "email.teste", JobId);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = "";
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken) : "";
            return await respond(cancellationToken);
        }
    }

    /// <summary>Corpo de resposta que falha ou não termina de chegar.</summary>
    private sealed class BrokenContent(Func<CancellationToken, Task> read) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => read(CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => read(cancellationToken);
        protected override bool TryComputeLength(out long length) => (length = 0) != 0;
    }

    private sealed class CapturingLogger : ILogger<BrevoEmailSender>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel} {formatter(state, exception)} {exception}");
    }

    private static async Task<(EmailSendResult Result, StubHandler Handler, CapturingLogger Logs)> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> respond, CancellationToken cancellationToken, TimeSpan? timeout = null,
        EmailMessage? message = null, string apiKey = ApiKey)
    {
        var handler = new StubHandler(respond);
        var logs = new CapturingLogger();
        using var http = new HttpClient(handler);
        var options = new EmailOptions { ApiKey = apiKey, SenderEmail = "nao-responda@example.org", SenderName = "Helinho Teste" };
        var sender = new BrevoEmailSender(http, Options.Create(options), logs);
        Assert.Equal(TimeSpan.FromSeconds(10), http.Timeout);
        Assert.Equal(16 * 1024, http.MaxResponseContentBufferSize);
        http.Timeout = timeout ?? http.Timeout; // só os testes de tempo esgotado encurtam, para não esperar 10 s
        var result = await sender.SendAsync(message ?? Message, cancellationToken);

        // Nada de chave, destinatário, assunto, corpo nem texto da resposta de erro em log, no resultado ou no ToString da mensagem.
        string[] secrets = [ApiKey, Message.Recipient, Message.Subject, "corpo-html-sintetico", Message.TextBody, ProviderEcho];
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, string.Join('\n', logs.Lines) + result + Message));
        return (result, handler, logs);
    }

    private static Func<CancellationToken, Task<HttpResponseMessage>> Respond(HttpStatusCode status, string body, string? reset = null) => _ =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (reset is not null) response.Headers.Add("x-sib-ratelimit-reset", reset);
        return Task.FromResult(response);
    };

    private static string Error(string code) => $$"""{"code":"{{code}}","message":"{{ProviderEcho}} {{Message.Recipient}}"}""";

    [Fact]
    public async Task Envia_no_formato_da_API_e_devolve_o_id_da_mensagem()
    {
        var (result, handler, logs) = await SendAsync(Respond(HttpStatusCode.Created, """{"messageId":"<202611.1234@relay.example.org>"}"""), Ct);

        Assert.Equal(new EmailSendResult(EmailSendStatus.Sent, "202611.1234@relay.example.org"), result);
        var request = Assert.IsType<HttpRequestMessage>(handler.Request);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", request.RequestUri?.AbsoluteUri);
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(handler.Body).RootElement;
        Assert.Equal("nao-responda@example.org", body.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Helinho Teste", body.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal(Message.Recipient, Assert.Single(body.GetProperty("to").EnumerateArray()).GetProperty("email").GetString());
        Assert.Equal(Message.Subject, body.GetProperty("subject").GetString());
        Assert.Equal(Message.HtmlBody, body.GetProperty("htmlContent").GetString());
        Assert.Equal(Message.TextBody, body.GetProperty("textContent").GetString());
        Assert.Equal("email.teste", Assert.Single(body.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Equal(JobId.ToString(), body.GetProperty("headers").GetProperty("idempotencyKey").GetString());
        Assert.StartsWith("Information", Assert.Single(logs.Lines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, "invalid_parameter", EmailSendStatus.PermanentFailure, "invalid_parameter")]
    [InlineData(400, "codigo_desconhecido", EmailSendStatus.PermanentFailure, "http_400")]
    [InlineData(404, "document_not_found", EmailSendStatus.PermanentFailure, "document_not_found")]
    [InlineData(400, "duplicate_parameter", EmailSendStatus.Sent, "duplicate_parameter")]
    [InlineData(400, "duplicate_request", EmailSendStatus.Sent, "duplicate_request")]
    [InlineData(400, "not_enough_credits", EmailSendStatus.ProviderSuspended, "not_enough_credits")]
    [InlineData(401, "unauthorized", EmailSendStatus.ProviderSuspended, "unauthorized")]
    [InlineData(402, "", EmailSendStatus.ProviderSuspended, "http_402")]
    [InlineData(403, "account_under_validation", EmailSendStatus.ProviderSuspended, "account_under_validation")]
    [InlineData(429, "", EmailSendStatus.TransientFailure, "http_429")]
    [InlineData(500, "", EmailSendStatus.TransientFailure, "http_500")]
    [InlineData(503, "", EmailSendStatus.TransientFailure, "http_503")]
    public async Task Classifica_cada_resposta_de_erro(int status, string code, EmailSendStatus expected, string expectedCode)
    {
        var (result, handler, logs) = await SendAsync(Respond((HttpStatusCode)status, code.Length > 0 ? Error(code) : "<html>erro</html>"), Ct);

        Assert.Equal(new EmailSendResult(expected, ErrorCode: expectedCode), result);
        Assert.Equal(1, handler.Calls); // uma tentativa: repetir é da fila
        var line = Assert.Single(logs.Lines);
        Assert.Contains($"HTTP {status}", line, StringComparison.Ordinal);
        Assert.StartsWith(expected == EmailSendStatus.Sent ? "Information" : status == 404 ? "Error" : "Warning", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData("999999", 3600)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData(null, null)]
    public async Task No_429_devolve_a_espera_pedida_pela_Brevo_com_teto(string? reset, int? expectedSeconds)
    {
        var (result, _, _) = await SendAsync(Respond(HttpStatusCode.TooManyRequests, "{}", reset), Ct);

        Assert.Equal(expectedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null, result.RetryAfter);
    }

    [Fact]
    public async Task Sem_chave_de_idempotencia_a_duplicata_e_recusa_e_o_corpo_nao_leva_tags_nem_headers()
    {
        var plain = Message with { Tag = null, IdempotencyKey = null };

        var (result, handler, _) = await SendAsync(Respond(HttpStatusCode.BadRequest, Error("duplicate_parameter")), Ct, message: plain);

        Assert.Equal(new EmailSendResult(EmailSendStatus.PermanentFailure, ErrorCode: "duplicate_parameter"), result);
        var body = JsonDocument.Parse(handler.Body).RootElement;
        Assert.False(body.TryGetProperty("tags", out _) || body.TryGetProperty("headers", out _));
    }

    /// <summary>
    /// O status decide. Depois de um 2xx a Brevo já aceitou: corpo acima de 16 KB, cortado, atrasado, vazio ou fora do
    /// formato continua <c>Sent</c> (repetir mandaria o e-mail em dobro). O corpo "grande" traz <c>messageId</c> e
    /// <c>code</c> válidos no começo: se o teto não valesse, eles apareceriam no resultado.
    /// </summary>
    [Theory]
    [InlineData("grande", 201, EmailSendStatus.Sent, "no_message_id")]
    [InlineData("queda", 201, EmailSendStatus.Sent, "no_message_id")]
    [InlineData("atraso", 201, EmailSendStatus.Sent, "no_message_id")]
    [InlineData("vazio", 202, EmailSendStatus.Sent, "no_message_id")]
    [InlineData("lista", 201, EmailSendStatus.Sent, "no_message_id")]
    [InlineData("grande", 500, EmailSendStatus.TransientFailure, "http_500")]
    [InlineData("queda", 400, EmailSendStatus.PermanentFailure, "http_400")]
    [InlineData("atraso", 429, EmailSendStatus.TransientFailure, "http_429")]
    [InlineData("vazio", 302, EmailSendStatus.PermanentFailure, "http_302")]
    public async Task Corpo_ilegivel_nao_muda_o_que_o_status_ja_disse(string body, int status, EmailSendStatus expected, string expectedCode)
    {
        HttpContent content = body switch
        {
            "grande" => new StringContent($$"""{"messageId":"<1@relay.example.org>","code":"invalid_parameter","pad":"{{new string('a', 17 * 1024)}}"}"""),
            "queda" => new BrokenContent(_ => throw new IOException("a conexão caiu")),
            "atraso" => new BrokenContent(token => Task.Delay(System.Threading.Timeout.Infinite, token)),
            "lista" => new StringContent("[]"),
            _ => new StringContent(""),
        };

        var (result, handler, _) = await SendAsync(
            _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }),
            Ct,
            body == "atraso" ? TimeSpan.FromMilliseconds(300) : null);

        Assert.Equal(new EmailSendResult(expected, ErrorCode: expectedCode), result);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_depois_de_aceito_continua_enviado()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new BrokenContent(async token =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(System.Threading.Timeout.Infinite, token);
        });

        var (result, _, _) = await SendAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = content }), cancellation.Token);

        Assert.Equal(new EmailSendResult(EmailSendStatus.Sent, ErrorCode: "no_message_id"), result);
    }

    [Fact]
    public async Task Chave_que_nao_cabe_num_cabecalho_suspende_sem_lancar_e_sem_chamar_a_Brevo()
    {
        var (result, handler, logs) = await SendAsync(Respond(HttpStatusCode.Created, "{}"), Ct, apiKey: "chave\r\ninjetada: valor");

        Assert.Equal(new EmailSendResult(EmailSendStatus.ProviderSuspended, ErrorCode: "invalid_api_key"), result);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain("injetada", string.Join('\n', logs.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tempo_esgotado_e_falha_de_rede_sao_transitorios()
    {
        // A resposta nunca chega: quem interrompe é o tempo limite do HttpClient, não o token do chamador.
        var (timeout, _, _) = await SendAsync(
            async token =>
            {
                await Task.Delay(System.Threading.Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.Created);
            },
            Ct,
            TimeSpan.FromMilliseconds(50));
        var (network, _, _) = await SendAsync(_ => throw new HttpRequestException(HttpRequestError.ConnectionError), Ct);

        Assert.Equal(new EmailSendResult(EmailSendStatus.TransientFailure, ErrorCode: "timeout"), timeout);
        Assert.Equal(new EmailSendResult(EmailSendStatus.TransientFailure, ErrorCode: "network_connectionerror"), network);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_interrompe_o_envio_e_sobe_como_excecao()
    {
        using var cancellation = new CancellationTokenSource();

        var sending = SendAsync(
            async token =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(System.Threading.Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.Created);
            },
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
    }
}
