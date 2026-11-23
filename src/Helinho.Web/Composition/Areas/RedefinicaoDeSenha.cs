using System.Net;
using System.Threading.RateLimiting;
using Helinho.Infrastructure.DependencyInjection;

namespace Helinho.Web.Composition;

/// <summary>Área Redefinição de senha (ADR-0003 §11.1 e §11.6): o caso de uso e o limite por IP do formulário anônimo.</summary>
public static class RedefinicaoDeSenhaArea
{
    public const string RateLimitPolicy = "recuperar-senha";

    public const int PedidosPorIp = 5;

    public static TimeSpan JanelaPorIp { get; } = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddRedefinicaoDeSenha(this IServiceCollection services)
    {
        services.AddPasswordResetRequests();
        // Soma uma política às opções que a área Identidade já configura (o 429 vem de lá). O IP é o resolvido pela
        // área Hospedagem. Em Razor Pages o atributo vale para a página inteira: o GET fica sem limite.
        services.AddRateLimiter(o => o.AddPolicy(RateLimitPolicy, context => HttpMethods.IsPost(context.Request.Method)
            ? RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress ?? IPAddress.None,
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PedidosPorIp, Window = JanelaPorIp, QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter(IPAddress.None)));
        return services;
    }
}
