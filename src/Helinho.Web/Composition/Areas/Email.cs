using Helinho.Infrastructure.Email;

namespace Helinho.Web.Composition;

/// <summary>
/// Área E-mail (ADR-0003 §4.4 e §7): o cliente HTTP tipado da Brevo, porque só o Web tem o <c>Microsoft.Extensions.Http</c>,
/// e a regra de ambiente. A escolha Fake/Brevo e a validação ficam na infraestrutura (<c>AddEmailSender</c>).
/// </summary>
public static partial class EmailArea
{
    public static IServiceCollection AddEmail(this IServiceCollection services)
    {
        // Sem redirecionamento: a chave não segue para outro endereço. Os valores dos cabeçalhos já saem
        // mascarados no log do HttpClient (padrão desde o .NET 9).
        services.AddHttpClient<BrevoEmailSender>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

        // Em Development o provedor é sempre Fake: uma chave real exportada no shell não envia e-mail por acidente.
        services.AddOptions<EmailOptions>().PostConfigure<IHostEnvironment, ILoggerFactory>((options, environment, loggers) =>
        {
            if (environment.IsDevelopment() && options.UsesBrevo)
            {
                options.Provider = EmailOptions.Fake;
                LogBrevoIgnored(loggers.CreateLogger(typeof(EmailArea)));
            }
        });
        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email:Provider=Brevo ignorado em Development: o envio continua Fake.")]
    private static partial void LogBrevoIgnored(ILogger logger);
}
