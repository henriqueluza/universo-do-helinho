using System.Net;
using System.Text;
using System.Text.Json;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.UnitTests;

/// <summary>
/// Issue #249: o adaptador da Anthropic (ADR-0004 §6.2). Nenhum teste chama a API: um <see cref="HttpMessageHandler"/>
/// falso devolve respostas escritas à mão, no formato documentado da Messages API.
/// </summary>
public sealed class AnthropicQuestionDraftGeneratorTests : IDisposable
{
    private const string ApiKey = "chave-sintetica-de-teste";
    private const string Model = "modelo-sintetico-configurado";
    private const string Subject = "assunto-sintetico-reciclagem";
    private const string Drafts =
        """{"questoes":[{"enunciado":"Enunciado gerado sintético?","a":"Um","b":"Dois","c":"Três","d":"Quatro","correta":"B","explicacao":"Explicação gerada sintética."}]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Recorder _http = new();
    private readonly Logs _logs = new();
    public void Dispose() => _http.Dispose();

    private sealed class Recorder : HttpMessageHandler
    {
        public Queue<Func<CancellationToken, Task<HttpResponseMessage>>> Responses { get; } = new();
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
        public TimeSpan Latency { get; set; }

        public void Enqueue(HttpStatusCode status, string body, params (string Name, string Value)[] headers) => Responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("request-id", "req_sintetico");
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, cancellationToken);
            }

            return await Responses.Dequeue()(cancellationToken);
        }
    }

    private sealed class Logs : ILogger<AnthropicQuestionDraftGenerator>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + exception);
    }

    private sealed class Monitor(AiOptions value) : IOptionsMonitor<AiOptions>
    {
        public AiOptions CurrentValue => value;
        public AiOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AiOptions, string?> listener) => null;
    }

    private AnthropicQuestionDraftGenerator Generator(int timeoutSeconds = 30) =>
        new(new HttpClient(_http), new Monitor(new AiOptions { Provider = AiOptions.Anthropic, ApiKey = ApiKey, Model = Model, TimeoutSeconds = timeoutSeconds }), _logs);

    private static QuestionDraftRequest Request(int count = 3) =>
        QuestionDraftRequest.Create(QuizType.Ambiental, AgeBand.Age8To10, count, Subject, ["Enunciado existente sintético?"]);

    /// <summary>Corpo de sucesso da Messages API, com o JSON das questões no bloco de texto.</summary>
    private static string Message(string text = Drafts, string stopReason = "end_turn") => JsonSerializer.Serialize(new
    {
        id = "msg_sintetico",
        type = "message",
        role = "assistant",
        model = "modelo-sintetico-que-respondeu",
        content = new[] { new { type = "text", text } },
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 1234, output_tokens = 567 },
    });

    private static string Error(string type, string? errorCode = null) => JsonSerializer.Serialize(new
    {
        type = "error",
        error = new { type, message = "mensagem-do-provedor-sintetica", details = errorCode is null ? null : new { error_code = errorCode } },
        request_id = "req_sintetico",
    });

    private async Task<QuestionDraftFailure> FailureAsync(int timeoutSeconds = 30) =>
        (await Assert.ThrowsAsync<QuestionDraftGenerationException>(() => Generator(timeoutSeconds).GenerateAsync(Request(), Ct))).Reason;

    [Fact]
    public async Task Sucesso_devolve_os_rascunhos_e_o_uso_e_envia_so_o_prompt_do_montador_com_o_modelo_configurado()
    {
        _http.Enqueue(HttpStatusCode.OK, Message());
        _http.Latency = TimeSpan.FromMilliseconds(60);
        // As variáveis do SDK não valem: endereço e chave são os passados na construção do cliente (ADR-0004 §6.2).
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://desvio.example.org");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "chave-de-ambiente-sintetica");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "token-de-ambiente-sintetico");
        QuestionDraftBatch batch;
        try
        {
            batch = await Generator().GenerateAsync(Request(count: 3), Ct);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
        }

        var draft = Assert.Single(batch.Drafts);
        Assert.Equal(("Enunciado gerado sintético?", 1), (draft.Statement, draft.CorrectIndex));
        Assert.Equal(("Anthropic", "modelo-sintetico-que-respondeu", 1234, 567, "req_sintetico", "gerar-questoes.v1"),
            (batch.Usage.Provider, batch.Usage.Model, batch.Usage.InputTokens, batch.Usage.OutputTokens, batch.Usage.ProviderRequestId, batch.PromptVersion));
        // A duração cobre a chamada: a resposta falsa demora 60 ms (folga de 10 ms para a granularidade do relógio).
        Assert.InRange(batch.Usage.Duration, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(30));

        var (request, body) = Assert.Single(_http.Requests);
        var prompt = QuestionPromptBuilder.Build(Request(count: 3));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal((HttpMethod.Post, "https://api.anthropic.com/v1/messages"), (request.Method, request.RequestUri?.AbsoluteUri));
        Assert.Equal(Model, root.GetProperty("model").GetString());
        Assert.Equal(1024 + (800 * 3), root.GetProperty("max_tokens").GetInt32());
        Assert.Contains(prompt.SystemText, root.GetProperty("system").ToString(), StringComparison.Ordinal);
        Assert.Equal(("user", prompt.UserText), (root.GetProperty("messages")[0].GetProperty("role").GetString(), root.GetProperty("messages")[0].GetProperty("content").GetString()));
        Assert.Equal(1, root.GetProperty("messages").GetArrayLength());
        Assert.Equal(("json_schema", "low"), (root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString(),
            root.GetProperty("output_config").GetProperty("effort").GetString()));
        Assert.Equal($"Bearer {ApiKey}", request.Headers.Authorization?.ToString());
        string[] fromEnvironment = ["chave-de-ambiente-sintetica", "token-de-ambiente-sintetico"];
        Assert.All(fromEnvironment, value => Assert.DoesNotContain(request.Headers, h => h.Value.Any(v => v.Contains(value, StringComparison.Ordinal))));
        // Nada além do necessário: sem ferramentas, sem amostragem, sem streaming, sem identificador de usuário.
        string[] allowed = ["model", "max_tokens", "system", "messages", "output_config"];
        Assert.Empty(root.EnumerateObject().Select(p => p.Name).Except(allowed));
    }

    [Fact]
    public async Task A_chave_vai_so_no_cabecalho_de_autenticacao_e_nada_sensivel_vai_para_o_log()
    {
        _http.Enqueue(HttpStatusCode.OK, Message());
        _http.Enqueue(HttpStatusCode.Unauthorized, Error("authentication_error"));

        await Generator().GenerateAsync(Request(), Ct);
        Assert.Equal(QuestionDraftFailure.Misconfigured, await FailureAsync());

        var (request, body) = _http.Requests[0];
        var withKey = request.Headers.Where(h => h.Value.Any(v => v.Contains(ApiKey, StringComparison.Ordinal))).Select(h => h.Key);
        Assert.Equal(["Authorization"], withKey);
        Assert.DoesNotContain(ApiKey, body + request.RequestUri, StringComparison.Ordinal);
        Assert.True(request.Headers.Contains("anthropic-version"));
        var log = string.Join('\n', _logs.Lines);
        Assert.Contains("req_sintetico", log, StringComparison.Ordinal);
        string[] secrets = [ApiKey, Subject, "Enunciado existente", "Enunciado gerado", "Explicação gerada", "mensagem-do-provedor-sintetica", "Você escreve questões"];
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, log, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("texto que não é JSON", "end_turn", QuestionDraftFailure.InvalidOutput)]
    [InlineData("""{"questoes":"fora do esquema"}""", "end_turn", QuestionDraftFailure.InvalidOutput)]
    [InlineData("""{"questoes":[{"enunciado":"cortado""", "max_tokens", QuestionDraftFailure.InvalidOutput)]
    [InlineData("Não posso ajudar com isso.", "refusal", QuestionDraftFailure.Refused)]
    public async Task Saida_fora_do_esquema_ou_recusa_vira_falha_tipada(string text, string stopReason, QuestionDraftFailure expected)
    {
        _http.Enqueue(HttpStatusCode.OK, Message(text, stopReason));

        Assert.Equal(expected, await FailureAsync());
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Item_malicioso_na_saida_chega_ao_validador_que_o_descarta()
    {
        var malicious = Drafts.Replace("Enunciado gerado sintético?", "<script>alert(1)</script>", StringComparison.Ordinal);
        _http.Enqueue(HttpStatusCode.OK, Message(malicious));

        var batch = await Generator().GenerateAsync(Request(), Ct);
        var result = QuestionDraftValidator.Validate(batch.Drafts, [], 3);

        Assert.Empty(result.Accepted);
        Assert.Equal(1, result.Rejections[DraftRejection.UnsafeText]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", null, QuestionDraftFailure.Misconfigured)]
    [InlineData(HttpStatusCode.Forbidden, "permission_error", null, QuestionDraftFailure.Misconfigured)]
    [InlineData(HttpStatusCode.NotFound, "not_found_error", null, QuestionDraftFailure.Misconfigured)]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", null, QuestionDraftFailure.Misconfigured)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "request_too_large", null, QuestionDraftFailure.Misconfigured)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "invalid_request_error", null, QuestionDraftFailure.Misconfigured)]
    // 408 e 409 o SDK repetiria por padrão: são passageiros, não configuração errada.
    [InlineData(HttpStatusCode.RequestTimeout, "timeout_error", null, QuestionDraftFailure.ProviderUnavailable)]
    [InlineData(HttpStatusCode.Conflict, "conflict_error", null, QuestionDraftFailure.ProviderUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", null, QuestionDraftFailure.RateLimited)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", "enforced_spend_limit_reached", QuestionDraftFailure.ProviderUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", null, QuestionDraftFailure.ProviderUnavailable)]
    [InlineData((HttpStatusCode)529, "overloaded_error", null, QuestionDraftFailure.ProviderUnavailable)]
    public async Task Erro_do_provedor_vira_o_motivo_fechado_em_uma_tentativa(HttpStatusCode status, string type, string? errorCode, QuestionDraftFailure expected)
    {
        _http.Enqueue(status, Error(type, errorCode), ("retry-after", "7"));

        Assert.Equal(expected, await FailureAsync());
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Erro_de_conexao_vira_ProviderUnavailable_em_uma_tentativa()
    {
        _http.Responses.Enqueue(_ => throw new HttpRequestException("falha de conexão sintética"));
        _http.Responses.Enqueue(_ => throw new HttpRequestException("falha de conexão sintética"));

        Assert.Equal(QuestionDraftFailure.ProviderUnavailable, await FailureAsync());
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Prazo_esgotado_vira_Timeout_e_cancelamento_de_quem_chamou_segue_como_cancelamento()
    {
        Func<CancellationToken, Task<HttpResponseMessage>> hang = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        _http.Responses.Enqueue(hang);
        _http.Responses.Enqueue(hang);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        caller.CancelAfter(TimeSpan.FromMilliseconds(100));

        Assert.Equal(QuestionDraftFailure.Timeout, await FailureAsync(timeoutSeconds: 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Generator().GenerateAsync(Request(), caller.Token));
        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Requisicao_invalida_e_recusada_antes_de_qualquer_chamada()
    {
        var invalid = new QuestionDraftRequest(QuizType.Ambiental, AgeBand.Age6To7, 500, null, []);

        await Assert.ThrowsAsync<DomainException>(() => Generator().GenerateAsync(invalid, Ct));
        Assert.Empty(_http.Requests);
    }
}
