using Helinho.Application.Abstractions;
using Helinho.Application.Ai;

namespace Helinho.Web.Composition;

/// <summary>
/// Área IA (ADR-0004 §14): a regra de ambiente da geração de rascunhos. As opções, a validação e a escolha do gerador
/// ficam na infraestrutura (<c>AddQuestionDraftGeneration</c>).
/// </summary>
public static partial class IaArea
{
    public static IServiceCollection AddIa(this IServiceCollection services)
    {
        // Em Development o provedor real nunca é usado: uma chave exportada no shell não gera custo por acidente.
        // Roda antes da validação, então Anthropic sem chave não derruba o ambiente de quem desenvolve.
        services.AddOptions<AiOptions>().PostConfigure<IHostEnvironment, ILoggerFactory>((options, environment, loggers) =>
        {
            if (environment.IsDevelopment() && options.UsesAnthropic)
            {
                options.Provider = AiOptions.Fake;
                LogRealProviderIgnored(loggers.CreateLogger(typeof(IaArea)));
            }
        });
        // Casos de uso da IA (ADR-0004 §10.2). A página que os chama fica sob a política Panel.
        services.AddScoped<RequestAiDrafts>();
        services.AddScoped<GetAiDraftJob>();
        services.AddScoped<ReviewAiDrafts>();
        services.AddScoped<SuggestSingleDraft>();
        // Tratador da raia ai.question-drafts: o trabalhador da fila o descobre por IJobHandler (ADR-0003 §6.6).
        services.AddScoped<IJobHandler, ProcessAiDraftJob>();
        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ai:Provider=Anthropic ignorado em Development: a geração usa o Fake.")]
    private static partial void LogRealProviderIgnored(ILogger logger);
}
