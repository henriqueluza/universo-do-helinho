using Helinho.Application.Administradores;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class IdentityExtensions
{
    /// <summary>Liga os stores do Identity ao <see cref="AppDbContext"/>, sem expor o tipo concreto à camada Web.</summary>
    public static IdentityBuilder AddAppIdentityStores(this IdentityBuilder builder) =>
        builder.AddEntityFrameworkStores<AppDbContext>();

    /// <summary>Casos de uso de administradores do painel e a porta sobre o Identity.</summary>
    public static IServiceCollection AddAdminAccounts(this IServiceCollection services)
    {
        services.AddScoped<IAdminAccountGateway, AdminAccountGateway>();
        services.AddScoped<CriarAdministradorHandler>();
        services.AddScoped<ListarAdministradoresHandler>();
        return services;
    }
}
