using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Helinho.Application.Email;
using Helinho.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Composition;

/// <summary><c>Brevo:WebhookSecret</c> (ADR-0003 §7). É classe, não record: não tem <c>ToString</c> que imprima o segredo.</summary>
public sealed class BrevoWebhookOptions
{
    public const int MinSecretLength = 32;

    public string Secret { get; set; } = "";

    /// <summary>Vazio desliga o webhook. Preenchido, vai num cabeçalho HTTP: 32 caracteres ASCII visíveis ou mais.</summary>
    public bool IsValid => Secret.Length == 0 || (Secret.Length >= MinSecretLength && Secret.All(c => c is > ' ' and <= '~'));
}

/// <summary>
/// Área Webhook da Brevo (ADR-0003 §12, D18): retorno de entrega dos e-mails, preparado e desligado. Fica fora de
/// <c>/api</c> e da descrição OpenAPI do jogo, e só existe quando há segredo configurado.
/// </summary>
public static partial class BrevoWebhookArea
{
    public const string Route = "/webhooks/brevo";
    public const string SecretHeader = "X-Helinho-Webhook-Secret";
    public const string RateLimitPolicy = "brevo-webhook";
    public const int MaxBodyBytes = 64 * 1024;
    public const int CallsPerMinute = 60;

    public static IServiceCollection AddBrevoWebhook(this IServiceCollection services)
    {
        services.AddEmailFeedback();
        services.AddOptions<BrevoWebhookOptions>()
            .Configure<IConfiguration>((options, configuration) => options.Secret = configuration["Brevo:WebhookSecret"] ?? "")
            .Validate(o => o.IsValid, "Brevo:WebhookSecret, quando preenchido, tem 32 caracteres ou mais, sem espaço nem quebra de linha.")
            .ValidateOnStart();

        // §11.6: por IP, já resolvido pela área Hospedagem. Vale antes da conferência do segredo.
        services.AddRateLimiter(o => o.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress ?? IPAddress.None,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = CallsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }

    public static WebApplication MapBrevoWebhook(this WebApplication app)
    {
        var secret = app.Services.GetRequiredService<IOptions<BrevoWebhookOptions>>().Value.Secret;
        if (secret.Length == 0)
        {
            return app; // §12: sem segredo a rota não existe (404). Não há modo "sem segredo".
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BrevoWebhookArea));
        // Chamada de máquina: sem sessão do painel e sem antiforgery; quem autentica é o cabeçalho próprio.
        app.MapPost(Route, (HttpContext context, IEmailFeedbackRecorder recorder) => HandleAsync(context, expected, recorder, logger))
            .AllowAnonymous().DisableAntiforgery().ExcludeFromDescription().RequireRateLimiting(RateLimitPolicy);
        return app;
    }

    private static async Task<IResult> HandleAsync(HttpContext context, byte[] expected, IEmailFeedbackRecorder recorder, ILogger logger)
    {
        // SHA-256 dos dois lados: a comparação em tempo constante não vaza nem o tamanho do segredo (§12).
        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers[SecretHeader].ToString()));
        if (!CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            LogRejected(logger);
            return Results.Unauthorized();
        }

        var buffer = new byte[MaxBodyBytes + 1];
        if (context.Request.ContentLength > MaxBodyBytes
            || await context.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, context.RequestAborted) is var read && read > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (ReadEvent(buffer.AsMemory(0, read)) is not var (providerEvent, messageId))
        {
            return Results.BadRequest();
        }

        // Evento fora da lista, corpo sem os campos, id desconhecido e evento repetido: 200 e nada muda (§12).
        var jobId = providerEvent is null || messageId is null
            ? null
            : await recorder.RecordAsync(providerEvent, messageId, context.RequestAborted);
        if (jobId is { } id)
        {
            LogRecorded(logger, providerEvent, id);
        }
        else
        {
            LogIgnored(logger);
        }

        return Results.Ok();
    }

    /// <summary>
    /// Lê só <c>event</c> e <c>message-id</c>; <c>email</c>, <c>subject</c> e <c>reason</c> não saem do corpo (§12).
    /// Devolve <c>null</c> quando o corpo não é JSON, e campos nulos quando é JSON de outro formato (o envio em lote, por exemplo).
    /// </summary>
    private static (string? Event, string? MessageId)? ReadEvent(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object ? (Text(root, "event"), Text(root, "message-id")) : (null, null);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook da Brevo: retorno {Feedback} gravado no trabalho {JobId}.")]
    private static partial void LogRecorded(ILogger logger, string? feedback, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook da Brevo: chamada aceita sem efeito (evento fora da lista, mensagem desconhecida ou já marcada).")]
    private static partial void LogIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook da Brevo: chamada recusada, segredo ausente ou incorreto.")]
    private static partial void LogRejected(ILogger logger);
}
