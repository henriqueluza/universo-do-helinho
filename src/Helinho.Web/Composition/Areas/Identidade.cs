using System.Net;
using System.Threading.RateLimiting;
using Helinho.Application.Identidade;
using Helinho.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;

namespace Helinho.Web.Composition;

/// <summary>Área Identidade: login da API (P1, P2, P4), sessão por token opaco e limite de taxa do login (T3).</summary>
public static class IdentidadeArea
{
    public const string LoginRateLimitPolicy = "api-login";

    public static IServiceCollection AddIdentidade(this IServiceCollection services)
    {
        services.AddApiLogin();
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
        });
        return services;
    }

    public static WebApplication MapIdentidade(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }
}
