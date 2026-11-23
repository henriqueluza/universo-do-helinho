using System.Net;
using System.Threading.RateLimiting;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace Helinho.Web.Composition;

/// <summary>Área Identidade: login da API (P1, P2, P4), sessão por token opaco, limites de taxa (T3, T12) e links de acesso.</summary>
public static class IdentidadeArea
{
    public const string LoginRateLimitPolicy = "api-login";
    public const string DefinirSenhaRateLimitPolicy = "definir-senha";

    public static IServiceCollection AddIdentidade(this IServiceCollection services)
    {
        services.AddApiLogin();
        // AddApiLogin (AddIdentityCore) volta a registrar a fábrica padrão e apagaria a claim de unidade do cookie do painel.
        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, UnitClaimsPrincipalFactory>();
        services.AddTokensDeLink();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<ApiAuthOptions>();
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress ?? IPAddress.None,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.LoginRateLimit,
                        Window = limits.LoginRateWindow,
                        QueueLimit = 0,
                    });
            });
            // ADR-0003 §11.3: 10 envios a cada 15 minutos por IP. O GET só confere o link e não conta.
            o.AddPolicy(DefinirSenhaRateLimitPolicy, context => HttpMethods.IsPost(context.Request.Method)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress ?? IPAddress.None,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter(IPAddress.None));
        });
        return services;
    }

    public static WebApplication MapIdentidade(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }
}
