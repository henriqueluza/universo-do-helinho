using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Email;
using Helinho.Infrastructure.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.DependencyInjection;

public static class EmailExtensions
{
    /// <summary>
    /// Opções de e-mail validadas na subida e escolha do provedor (ADR-0003 §7). O cliente HTTP tipado do
    /// <see cref="BrevoEmailSender"/> é registrado pela área Email do Web, que tem o <c>Microsoft.Extensions.Http</c>.
    /// </summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                // Variável vazia vale como ausente: o padrão é Fake.
                options.Provider = configuration["Email:Provider"] is { Length: > 0 } provider ? provider : EmailOptions.Fake;
                options.ApiKey = configuration["Brevo:ApiKey"] ?? "";
                options.SenderEmail = configuration["Brevo:SenderEmail"] ?? "";
                options.SenderName = configuration["Brevo:SenderName"] ?? "";
                options.PublicBaseUrl = configuration["App:PublicBaseUrl"] ?? "";
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        services.AddSingleton<FakeEmailSender>();
        // Transitório: o cliente tipado da Brevo não pode ficar preso num singleton (IHttpClientFactory).
        // IOptionsMonitor é a mesma instância que o ValidateOnStart já validou.
        services.AddTransient<IEmailSender>(provider =>
            provider.GetRequiredService<IOptionsMonitor<EmailOptions>>().CurrentValue.UsesBrevo
                ? provider.GetRequiredService<BrevoEmailSender>()
                : provider.GetRequiredService<FakeEmailSender>());
        return services;
    }

    private sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
    {
        public ValidateOptionsResult Validate(string? name, EmailOptions options) =>
            options.Errors().ToList() is { Count: > 0 } errors ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
