namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área Gestao: só a Issue dessa área edita este arquivo.</summary>
public static class GestaoArea
{
    public static IServiceCollection AddGestao(this IServiceCollection services) => services;

    public static WebApplication MapGestao(this WebApplication app) => app;
}
