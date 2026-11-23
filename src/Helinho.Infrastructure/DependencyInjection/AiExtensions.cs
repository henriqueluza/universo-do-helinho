using System.Globalization;
using Helinho.Application.Ai;
using Helinho.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.DependencyInjection;

public static class AiExtensions
{
    /// <summary>
    /// Opções de IA validadas na subida e escolha do gerador pelo provedor (ADR-0004 §14). A regra de ambiente
    /// (<c>Development</c> nunca usa o provedor real) fica na área IA do Web, que tem o <c>IHostEnvironment</c>.
    /// </summary>
    public static IServiceCollection AddQuestionDraftGeneration(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var section = configuration.GetSection(AiOptions.Section);
                // Variável vazia vale como ausente: IA desligada e os padrões das cotas e do prazo.
                options.Provider = section[nameof(AiOptions.Provider)] is { } provider && !string.IsNullOrWhiteSpace(provider) ? provider : AiOptions.Off;
                options.ApiKey = section[nameof(AiOptions.ApiKey)] ?? "";
                options.Model = section[nameof(AiOptions.Model)] ?? "";
                options.TimeoutSeconds = Number(section[nameof(AiOptions.TimeoutSeconds)], options.TimeoutSeconds);
                options.DailyQuotaPerAdmin = Number(section[nameof(AiOptions.DailyQuotaPerAdmin)], options.DailyQuotaPerAdmin);
                options.DailyGlobalLimit = Number(section[nameof(AiOptions.DailyGlobalLimit)], options.DailyGlobalLimit);
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiOptions>, AiOptionsValidator>();

        services.AddSingleton<DisabledQuestionDraftGenerator>();
        // Os geradores Fake e Anthropic entram aqui pelas Issues deles. Até lá, e sempre com Off, a porta falha fechada.
        services.AddTransient<IQuestionDraftGenerator>(provider => provider.GetRequiredService<DisabledQuestionDraftGenerator>());
        return services;
    }

    /// <summary>Lido à mão, não pelo binder: o erro de conversão dele imprime o valor recebido. Texto que não é número vira 0, que a validação recusa.</summary>
    private static int Number(string? raw, int fallback) =>
        string.IsNullOrWhiteSpace(raw) ? fallback
        : int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private sealed class AiOptionsValidator : IValidateOptions<AiOptions>
    {
        public ValidateOptionsResult Validate(string? name, AiOptions options) =>
            options.Errors().ToList() is { Count: > 0 } errors ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
