using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class EmailTemplatesExtensions
{
    /// <summary>
    /// Renderizador dos e-mails de acesso. Quem chama entrega a base pública (<c>App:PublicBaseUrl</c>) e o rodapé,
    /// lidos das opções da Issue do adaptador de envio; aqui nenhuma chave de configuração é lida.
    /// </summary>
    public static IServiceCollection AddEmailTemplates(this IServiceCollection services, Func<IServiceProvider, EmailTemplateSettings> settings)
    {
        services.AddSingleton(provider => new EmailTemplateRenderer(settings(provider)));
        return services;
    }
}
