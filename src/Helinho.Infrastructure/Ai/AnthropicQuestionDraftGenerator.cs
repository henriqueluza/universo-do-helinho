using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Helinho.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.Ai;

/// <summary>
/// Adaptador da Messages API da Anthropic atrás da porta de geração (ADR-0004 §6.2), pelo SDK oficial. Uma chamada
/// por pedido, sem ferramentas, sem streaming e sem identificador de usuário: o corpo é o prompt do montador e o
/// esquema de saída. O modelo vem de <c>Ai:Model</c>; não há nome de modelo no código. A chave só vai no cabeçalho
/// de autenticação, e o log não traz chave, prompt, assunto nem resposta.
/// </summary>
public sealed partial class AnthropicQuestionDraftGenerator(
    HttpClient http, IOptionsMonitor<AiOptions> options, ILogger<AnthropicQuestionDraftGenerator> logger) : IQuestionDraftGenerator
{
    public const string Provider = "Anthropic";

    /// <summary>Constante: nem configuração nem a variável <c>ANTHROPIC_BASE_URL</c> mudam o destino da chave.</summary>
    public const string BaseUrl = "https://api.anthropic.com";

    /// <summary>Teto de saída: uma base mais uma parcela por questão pedida, para o lote não sair cortado.</summary>
    private const int BaseMaxTokens = 1024;
    private const int MaxTokensPerQuestion = 800;

    // Quatro propriedades obrigatórias, não um vetor: o esquema da API não aceita minItems nem maxItems de 4 (ADR-0004 §6.3).
    private static readonly Dictionary<string, JsonElement> OutputSchema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
        """
        {
          "type": "object",
          "properties": {
            "questoes": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "enunciado": { "type": "string" },
                  "a": { "type": "string" },
                  "b": { "type": "string" },
                  "c": { "type": "string" },
                  "d": { "type": "string" },
                  "correta": { "type": "string", "enum": ["a", "b", "c", "d"] },
                  "explicacao": { "type": "string" }
                },
                "required": ["enunciado", "a", "b", "c", "d", "correta", "explicacao"],
                "additionalProperties": false
              }
            }
          },
          "required": ["questoes"],
          "additionalProperties": false
        }
        """) ?? throw new InvalidOperationException("Esquema de saída inválido.");

    public async Task<QuestionDraftBatch> GenerateAsync(QuestionDraftRequest request, CancellationToken cancellationToken)
    {
        var prompt = QuestionPromptBuilder.Build(request);
        var settings = options.CurrentValue;
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = BaseMaxTokens + (MaxTokensPerQuestion * request.Count),
            System = prompt.SystemText,
            Messages = [new UserMessageParam { Content = prompt.UserText }],
            OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = OutputSchema } },
        };
        // Uma tentativa por chamada. MaxRetries = 0 desliga a repetição do SDK, que inclui erro de conexão, 408 e 409;
        // a repetição só de 429 e 5xx é da Issue seguinte.
        var client = new AnthropicClient
        {
            ApiKey = settings.ApiKey,
            BaseUrl = BaseUrl,
            HttpClient = http,
            MaxRetries = 0,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };

        var watch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var response = await client.WithRawResponse.Messages.Create(parameters, deadline.Token);
            var message = await response.Deserialize(deadline.Token);
            var batch = ToBatch(message, prompt.Version, watch.Elapsed, response.RequestID);
            LogGenerated(logger, batch.Usage.InputTokens, batch.Usage.OutputTokens, (long)watch.Elapsed.TotalMilliseconds, response.RequestID);
            return batch;
        }
        catch (QuestionDraftGenerationException failure)
        {
            LogFailed(logger, failure.Reason, null);
            throw;
        }
        catch (Exception failure)
        {
            // Cancelamento de quem chamou segue como cancelamento; o prazo daqui vira Timeout.
            cancellationToken.ThrowIfCancellationRequested();
            var reason = Reason(failure, deadline.IsCancellationRequested);
            LogFailed(logger, reason, (failure as AnthropicApiException)?.RequestID);
            throw new QuestionDraftGenerationException(reason);
        }
    }

    private static QuestionDraftBatch ToBatch(Message message, string promptVersion, TimeSpan elapsed, string? requestId)
    {
        var stop = message.StopReason?.Raw();
        if (stop != "end_turn")
        {
            // Recusa (HTTP 200, cobrada) não melhora repetindo; max_tokens é saída cortada, fora do esquema.
            throw new QuestionDraftGenerationException(stop == "refusal" ? QuestionDraftFailure.Refused : QuestionDraftFailure.InvalidOutput);
        }

        var text = string.Concat(message.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
        var usage = new AiUsage(Provider, message.Model.Raw(), (int)message.Usage.InputTokens, (int)message.Usage.OutputTokens, elapsed, requestId);
        return new QuestionDraftBatch(QuestionDraftOutputParser.Parse(text), promptVersion, usage);
    }

    /// <summary>O 429 de teto de gasto não é limite de taxa: não tem <c>retry-after</c> e segue falhando até o mês virar.</summary>
    private static bool IsSpendLimit(Exception failure) =>
        failure is AnthropicServiceException { ErrorCode: { } code } && code.Contains("spend_limit", StringComparison.Ordinal);

    private static QuestionDraftFailure Reason(Exception failure, bool deadlineReached) => failure switch
    {
        OperationCanceledException or TimeoutException => QuestionDraftFailure.Timeout,
        _ when deadlineReached => QuestionDraftFailure.Timeout,
        AnthropicUnauthorizedException or AnthropicForbiddenException or AnthropicNotFoundException => QuestionDraftFailure.Misconfigured,
        _ when IsSpendLimit(failure) => QuestionDraftFailure.ProviderUnavailable,
        AnthropicRateLimitException => QuestionDraftFailure.RateLimited,
        // 408 e 409 são passageiros (o SDK os repetiria): indisponibilidade, não configuração errada.
        AnthropicRequestTimeoutException or AnthropicConflictException => QuestionDraftFailure.ProviderUnavailable,
        Anthropic4xxException => QuestionDraftFailure.Misconfigured,
        AnthropicInvalidDataException or JsonException => QuestionDraftFailure.InvalidOutput,
        _ => QuestionDraftFailure.ProviderUnavailable,
    };

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Geração por IA concluída: tokens de entrada={InputTokens}, de saída={OutputTokens}, duração={DurationMs} ms, request-id={RequestId}.")]
    private static partial void LogGenerated(ILogger logger, int inputTokens, int outputTokens, long durationMs, string? requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geração por IA falhou: motivo={Reason}, request-id={RequestId}.")]
    private static partial void LogFailed(ILogger logger, QuestionDraftFailure reason, string? requestId);
}
