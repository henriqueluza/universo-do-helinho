using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class EmailJobsExtensions
{
    /// <summary>
    /// Tratador de e-mail da fila (ADR-0003, item 8b) e a leitura dele sobre o Identity e o banco. Escopo por item,
    /// como o trabalhador pede (§6.6). Quem chama registra antes o renderizador (<c>AddEmailTemplates</c>).
    /// </summary>
    public static IServiceCollection AddEmailJobs(this IServiceCollection services)
    {
        services.AddScoped<IEmailSubjects, EmailSubjects>();
        services.AddScoped<IJobHandler>(provider => new EmailJobHandler(
            provider.GetRequiredService<IEmailSubjects>(), Renderer(provider),
            provider.GetRequiredService<IEmailSender>(), provider.GetRequiredService<IClock>()));
        return services;
    }

    /// <summary>
    /// Nulo quando <c>App:PublicBaseUrl</c> falta ou não serve de base (<see cref="PublicBaseUrl.Parse"/> recusa com
    /// <see cref="ArgumentException"/>). Só acontece com o envio Fake: com Brevo a subida já exigiu a base. O tratador
    /// é resolvido na subida pelo trabalhador, então isto não pode derrubar a aplicação; ele suspende os e-mails.
    /// </summary>
    private static EmailTemplateRenderer? Renderer(IServiceProvider provider)
    {
        try
        {
            return provider.GetRequiredService<EmailTemplateRenderer>();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
