using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Composition;

/// <summary>
/// Área Hospedagem: a aplicação atrás do proxy que termina o TLS. Cabeçalhos do proxy (esquema e IP do cliente),
/// HSTS, redirecionamento para HTTPS e cookies <c>Secure</c>. Entra por <see cref="IStartupFilter"/> porque precisa
/// rodar antes de tudo o que o <c>Program.cs</c> monta (arquivos estáticos, autenticação, limite de taxa).
/// </summary>
public static class HospedagemArea
{
    /// <summary>Prefixo das rotas de saúde: o healthcheck da plataforma chega em HTTP puro e não pode ser redirecionado.</summary>
    public const string HealthPrefix = "/health";

    public static IServiceCollection AddHospedagem(this IServiceCollection services)
    {
        services.AddOptions<ProxyOptions>()
            .BindConfiguration(ProxyOptions.Section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientIpHeader), "Proxy:ClientIpHeader não pode ser vazio.")
            .ValidateOnStart();

        services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));

        // O contêiner só escuta HTTP, então o middleware não descobre a porta HTTPS sozinho e deixaria de redirecionar.
        services.AddOptions<HttpsRedirectionOptions>().Configure<IConfiguration>((o, configuration) =>
            o.HttpsPort ??= configuration.GetValue<int?>("HTTPS_PORT") ?? 443);

        services.AddTransient<IStartupFilter, PipelineFilter>();
        return services;
    }

    private sealed class PipelineFilter(IHostEnvironment environment, IOptions<ProxyOptions> proxy) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            if (proxy.Value.TrustForwardedHeaders)
            {
                app.UseForwardedHeaders(TrustLastHop(proxy.Value));
            }

            if (!environment.IsDevelopment())
            {
                app.UseHsts();
                app.UseWhen(
                    context => !context.Request.Path.StartsWithSegments(HealthPrefix),
                    branch => branch.UseHttpsRedirection());
                // Vale para todo cookie da resposta: painel, antiforgery e TempData.
                app.UseCookiePolicy(new CookiePolicyOptions { Secure = CookieSecurePolicy.Always });
            }

            next(app);
        };

        /// <summary>
        /// A Railway não publica IPs estáveis do proxy, então não há lista de proxies conhecidos: confia-se no par
        /// imediato e só no último valor de cada cabeçalho (<c>ForwardLimit = 1</c>), que é o escrito pela borda.
        /// </summary>
        private static ForwardedHeadersOptions TrustLastHop(ProxyOptions proxy)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardedForHeaderName = proxy.ClientIpHeader,
                ForwardLimit = 1,
            };
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            return options;
        }
    }
}
