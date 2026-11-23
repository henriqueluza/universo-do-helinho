using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Composition;

/// <summary>
/// Tratador de e-mail da fila (ADR-0003, item 8b): liga o renderizador dos modelos à base pública e ao rodapé e
/// registra o tratador. Fica no Web porque a regra de <c>http://localhost</c> depende do ambiente.
/// </summary>
public static class EmailTratadorArea
{
    // PROPOSTA (§8.3): a identificação e o contato do rodapé são pergunta para a contratante; até lá, texto neutro.
    public const string DefaultOrganization = "FUNDHAS";
    public const string DefaultContact = "a unidade da FUNDHAS";

    public static IServiceCollection AddEmailTratador(this IServiceCollection services)
    {
        // A base vem só de App:PublicBaseUrl (§10.3), pelas mesmas opções que a subida valida com Brevo.
        services.AddEmailTemplates(provider => new EmailTemplateSettings(
            PublicBaseUrl.Parse(
                provider.GetRequiredService<IOptions<EmailOptions>>().Value.PublicBaseUrl,
                provider.GetRequiredService<IHostEnvironment>().IsDevelopment()),
            Footer(provider, "Email:Organization", DefaultOrganization),
            Footer(provider, "Email:Contact", DefaultContact)));
        services.AddEmailJobs();
        return services;
    }

    private static string Footer(IServiceProvider provider, string key, string fallback) =>
        provider.GetRequiredService<IConfiguration>()[key] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
}
