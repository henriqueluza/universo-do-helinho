using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Helinho.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.DependencyInjection;

public static class EmailJobsExtensions
{
    /// <summary>
    /// Tratador de e-mail da fila (ADR-0003, item 8b), a leitura dele sobre o Identity e o banco e o teto diário
    /// (<c>Email:DailyLimit</c>, validado na subida). Escopo por item, como o trabalhador pede (§6.6). Quem chama
    /// registra antes o renderizador (<c>AddEmailTemplates</c>).
    /// </summary>
    public static IServiceCollection AddEmailJobs(this IServiceCollection services)
    {
        services.AddOptions<EmailDailyLimitOptions>()
            .Configure<IConfiguration>((options, configuration) => options.Value = configuration[EmailDailyLimitOptions.Key]?.Trim() ?? "")
            .Validate(options => options.Limit is not null, $"{EmailDailyLimitOptions.Key} precisa ser um número inteiro maior ou igual a zero (0 ou vazio = sem teto).")
            .ValidateOnStart();
        services.TryAddScoped<PreviousGuardianEmailCleaner>(); // a área de alunos também o registra; aqui o tratador não depende dela
        services.AddScoped<IEmailSubjects, EmailSubjects>();
        services.AddScoped<IEmailDailyCount, EmailDailyCount>();

        // §15: dois registros da mesma classe, sem tipo repetido entre eles; cada um tem captura e laço próprios.
        foreach (var lane in new[] { EmailLane.Urgent, EmailLane.Others })
        {
            services.AddScoped<IJobHandler>(provider =>
            {
                var clock = provider.GetRequiredService<IClock>();
                var limit = provider.GetRequiredService<IOptions<EmailDailyLimitOptions>>().Value.Limit ?? 0;
                return new EmailJobHandler(
                    lane, new EmailDailyLimit(limit, provider.GetRequiredService<IEmailDailyCount>(), clock),
                    provider.GetRequiredService<IEmailSubjects>(), Renderer(provider), provider.GetRequiredService<IEmailSender>(), clock);
            });
        }

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
