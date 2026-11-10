namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área ApiHome: só a Issue dessa área edita este arquivo.</summary>
public static class ApiHomeArea
{
    public static IServiceCollection AddApiHome(this IServiceCollection services) => services;

    public static WebApplication MapApiHome(this WebApplication app) => app;
}
