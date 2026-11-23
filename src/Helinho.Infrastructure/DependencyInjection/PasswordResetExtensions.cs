using Helinho.Application.Identidade;
using Helinho.Infrastructure.Identidade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Helinho.Infrastructure.DependencyInjection;

public static class PasswordResetExtensions
{
    /// <summary>Pedido de redefinição de senha (ADR-0003 §11.1): a consulta sobre o Identity e a fila, e o caso de uso.</summary>
    public static IServiceCollection AddPasswordResetRequests(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordResetAccounts, PasswordResetAccounts>();
        services.AddScoped<PedirRedefinicaoHandler>();
        return services;
    }

    /// <summary>
    /// Aviso "sua senha foi alterada" (ADR-0003 §11.3): decora a porta de definir senha. Precisa vir depois de
    /// <c>AddPasswordLinks</c>, porque o último registro da porta é o que vale.
    /// </summary>
    public static IServiceCollection AddPasswordChangedNotice(this IServiceCollection services)
    {
        services.AddScoped<PasswordLinkGateway>();
        services.AddScoped<IPasswordLinkGateway>(sp => ActivatorUtilities.CreateInstance<PasswordChangeNotifyingGateway>(
            sp, sp.GetRequiredService<PasswordLinkGateway>()));
        return services;
    }
}
