namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área ApiPerfil: só a Issue dessa área edita este arquivo.</summary>
public static class ApiPerfilArea
{
    public static IServiceCollection AddApiPerfil(this IServiceCollection services) => services;

    public static WebApplication MapApiPerfil(this WebApplication app) => app;
}
