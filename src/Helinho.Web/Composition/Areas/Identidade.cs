namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área Identidade: só a Issue dessa área edita este arquivo.</summary>
public static class IdentidadeArea
{
    public static IServiceCollection AddIdentidade(this IServiceCollection services) => services;

    public static WebApplication MapIdentidade(this WebApplication app) => app;
}
