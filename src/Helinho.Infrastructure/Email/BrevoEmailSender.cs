using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Helinho.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// API transacional v3 da Brevo (ADR-0003 §4). Uma tentativa por chamada: repetir é da fila. Não loga nem devolve
/// destinatário, assunto, corpo, chave ou o campo <c>message</c> da resposta de erro, que pode repetir o destinatário.
/// </summary>
public sealed partial class BrevoEmailSender : IEmailSender
{
    /// <summary>Fixo no código: nenhuma configuração aponta o cliente, e a chave, para outro host.</summary>
    public static readonly Uri Endpoint = new("https://api.brevo.com/v3/smtp/email");

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const int MaxResponseBytes = 16 * 1024;
    private const int MaxRetryAfterSeconds = 3600;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly string[] AccountCodes = ["unauthorized", "not_enough_credits", "permission_denied", "account_under_validation"];
    private static readonly string[] DuplicateCodes = ["duplicate_parameter", "duplicate_request"];
    private static readonly string[] KnownCodes =
        [.. AccountCodes, .. DuplicateCodes, "invalid_parameter", "missing_parameter", "out_of_range", "method_not_allowed", "document_not_found"];

    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<BrevoEmailSender> _logger;

    public BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<BrevoEmailSender> logger)
    {
        // Valem para a resposta inteira (cabeçalhos e corpo); resposta maior que o limite vira falha transitória.
        http.Timeout = Timeout;
        http.MaxResponseContentBufferSize = MaxResponseBytes;
        (_http, _options, _logger) = (http, options.Value, logger);
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(Body(message), options: Json) };
        request.Headers.Add("api-key", _options.ApiKey);
        request.Headers.Add("accept", "application/json");

        var status = 0;
        EmailSendResult result;
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            var (messageId, code) = await ReadAsync(response, cancellationToken);
            result = Classify(status, messageId, code, message.IdempotencyKey is not null) with { RetryAfter = RetryAfter(response) };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new(EmailSendStatus.TransientFailure, ErrorCode: "timeout");
        }
        catch (HttpRequestException exception)
        {
            result = new(EmailSendStatus.TransientFailure, ErrorCode: $"network_{exception.HttpRequestError}".ToLowerInvariant());
        }

        LogOutcome(_logger, result.Status == EmailSendStatus.Sent ? LogLevel.Information : LogLevel.Warning, result.Status, status, result.ErrorCode);
        return result;
    }

    private object Body(EmailMessage message) => new
    {
        sender = new { email = _options.SenderEmail, name = _options.SenderName },
        to = new[] { new { email = message.Recipient } },
        subject = message.Subject,
        htmlContent = message.HtmlBody,
        textContent = message.TextBody,
        tags = message.Tag is { } tag ? new[] { tag } : null,
        headers = message.IdempotencyKey is { } key ? new { idempotencyKey = key.ToString() } : null,
    };

    /// <summary>Lê só <c>messageId</c> e <c>code</c>; corpo que não é o JSON esperado é ignorado.</summary>
    private static async Task<(string? MessageId, string? Code)> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return (Text(json, "messageId"), Text(json, "code"));
        }
        catch (JsonException)
        {
            return default;
        }

        static string? Text(JsonDocument json, string name) =>
            json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Tabela do ADR-0003 §4.2. O código da Brevo só é repassado se for conhecido; senão vira <c>http_&lt;status&gt;</c>.</summary>
    private static EmailSendResult Classify(int status, string? messageId, string? code, bool idempotent)
    {
        if (status is >= 200 and < 300)
        {
            return new(EmailSendStatus.Sent, messageId?.Trim('<', '>'));
        }

        var error = code is not null && KnownCodes.Contains(code) ? code : $"http_{status.ToString(CultureInfo.InvariantCulture)}";
        if (status == 400 && idempotent && DuplicateCodes.Contains(code))
        {
            // Repetição dentro da janela de idempotência: a Brevo já aceitou esta mensagem antes.
            return new(EmailSendStatus.Sent, ErrorCode: error);
        }

        var outcome = status is 401 or 402 or 403 || AccountCodes.Contains(code) ? EmailSendStatus.ProviderSuspended
            : status is 429 or >= 500 ? EmailSendStatus.TransientFailure
            : EmailSendStatus.PermanentFailure;
        return new(outcome, ErrorCode: error);
    }

    /// <summary><c>x-sib-ratelimit-reset</c>: segundos até o limite de taxa voltar; limitado, porque vem de fora.</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        && response.Headers.TryGetValues("x-sib-ratelimit-reset", out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryAfterSeconds))
            : null;

    [LoggerMessage(Message = "Envio pela Brevo: {Resultado}, HTTP {Status}, código {Codigo}.")]
    private static partial void LogOutcome(ILogger logger, LogLevel level, EmailSendStatus resultado, int status, string? codigo);
}
