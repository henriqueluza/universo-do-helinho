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

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

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
        // O prazo vale para o envio inteiro (cabeçalhos e corpo); do corpo da resposta só se leem os primeiros 16 KB.
        http.Timeout = RequestTimeout;
        http.MaxResponseContentBufferSize = MaxResponseBytes;
        (_http, _options, _logger) = (http, options.Value, logger);
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var status = 0;
        EmailSendResult result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_http.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(Body(message), options: Json) };
            request.Headers.Add("api-key", _options.ApiKey);
            request.Headers.Add("accept", "application/json");

            // Só os cabeçalhos: o status decide o resultado antes de qualquer leitura do corpo.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            status = (int)response.StatusCode;
            var (messageId, code) = await ReadAsync(response, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Depois de um 2xx a Brevo já aceitou: nem o cancelamento do chamador transforma isso em "não enviado".
                cancellationToken.ThrowIfCancellationRequested();
            }

            result = Classify(status, messageId, code, message.IdempotencyKey is not null) with { RetryAfter = RetryAfter(response) };
        }
        catch (FormatException)
        {
            // A chave tem caractere que não cabe num cabeçalho. O texto da exceção não é usado: pode trazer o valor.
            result = new(EmailSendStatus.ProviderSuspended, ErrorCode: "invalid_api_key");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new(EmailSendStatus.TransientFailure, ErrorCode: "timeout");
        }
        catch (HttpRequestException exception)
        {
            result = new(EmailSendStatus.TransientFailure, ErrorCode: $"network_{exception.HttpRequestError}".ToLowerInvariant());
        }

        var level = result.Status switch
        {
            EmailSendStatus.Sent => LogLevel.Information,
            EmailSendStatus.PermanentFailure when status != 400 => LogLevel.Error, // rota, método ou formato errados: defeito nosso
            _ => LogLevel.Warning,
        };
        LogOutcome(_logger, level, result.Status, status, result.ErrorCode);
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

    /// <summary>
    /// Lê só <c>messageId</c> e <c>code</c>, até 16 KB. Nunca lança: corpo grande demais, cortado, atrasado ou fora do
    /// formato não muda o que o status já disse.
    /// </summary>
    private static async Task<(string? MessageId, string? Code)> ReadAsync(HttpResponseMessage response, CancellationToken deadline)
    {
        try
        {
            await response.Content.LoadIntoBufferAsync(MaxResponseBytes, deadline);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: deadline);
            return (Text(json, "messageId"), Text(json, "code"));
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or IOException or OperationCanceledException)
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
            // Aceito é aceito: sem id legível continua enviado, para a fila não repetir uma mensagem que já saiu.
            var id = messageId?.Trim('<', '>') is { Length: > 0 } trimmed ? trimmed : null;
            return new(EmailSendStatus.Sent, id, ErrorCode: id is null ? "no_message_id" : null);
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
