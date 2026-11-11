using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Identity;

namespace Helinho.Web.Composition;

/// <summary>Slice 1d: Razor Pages, layout, Identity (cookie) e políticas por papel.</summary>
public static class Panel
{
    public const string LoginPath = "/entrar";

    public static IServiceCollection AddPanel(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.User.RequireUniqueEmail = false;
            })
            .AddAppIdentityStores()
            .AddClaimsPrincipalFactory<UnitClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "helinho.panel";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = LoginPath;
            options.AccessDeniedPath = "/acesso-negado";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(PanelPolicies.Panel, p => p.RequireRole(RoleNames.Master, RoleNames.Admin))
            .AddPolicy(PanelPolicies.MasterOnly, p => p.RequireRole(RoleNames.Master));

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "helinho.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/", PanelPolicies.Panel);
            options.Conventions.AllowAnonymousToFolder("/Conta");
            options.Conventions.AuthorizeFolder("/Unidades", PanelPolicies.MasterOnly);
            options.Conventions.AuthorizeFolder("/Administradores", PanelPolicies.MasterOnly);
            options.Conventions.AddPageRoute("/Conta/Entrar", LoginPath);
            options.Conventions.AddPageRoute("/Conta/Sair", "/sair");
            options.Conventions.AddPageRoute("/Conta/AcessoNegado", "/acesso-negado");
            options.Conventions.AddPageRoute("/Dashboard/Index", "/");
        });

        return services;
    }

    public static WebApplication MapPanel(this WebApplication app)
    {
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        return app;
    }
}
