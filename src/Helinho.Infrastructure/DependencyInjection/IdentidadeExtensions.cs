using Helinho.Application.Abstractions;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class IdentidadeExtensions
{
    /// <summary>Login da API: Identity (usuários, senha, bloqueio), sessão com token opaco e o caso de uso.</summary>
    public static IServiceCollection AddApiLogin(this IServiceCollection services)
    {
        // A configuração é lida de forma tardia, para os testes poderem sobrescrevê-la.
        services.AddSingleton(sp =>
        {
            var options = new ApiAuthOptions();
            sp.GetRequiredService<IConfiguration>().GetSection(ApiAuthOptions.Section).Bind(options);
            return options;
        });

        services.AddIdentityCore<AppUser>(o => o.Lockout.AllowedForNewUsers = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddOptions<IdentityOptions>().Configure<ApiAuthOptions>((o, auth) =>
        {
            o.Lockout.MaxFailedAccessAttempts = auth.MaxFailedAttempts;
            o.Lockout.DefaultLockoutTimeSpan = auth.LockoutDuration;
        });

        services.AddScoped<ILoginIdentityGateway, LoginIdentityGateway>();
        services.AddScoped<IStudentAccounts, StudentAccounts>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<IApiSessionValidator, ApiSessionValidator>();
        return services;
    }

    /// <summary>Definir senha pelo link do e-mail (convite e redefinição): a porta sobre o Identity e o caso de uso.</summary>
    public static IServiceCollection AddPasswordLinks(this IServiceCollection services)
    {
        services.AddScoped<IPasswordLinkGateway, PasswordLinkGateway>();
        services.AddScoped<DefinirSenhaHandler>();
        return services;
    }
}
