namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área PainelConteudo: só a Issue dessa área edita este arquivo.</summary>
public static class PainelConteudoArea
{
    public static IServiceCollection AddPainelConteudo(this IServiceCollection services) => services;

    public static WebApplication MapPainelConteudo(this WebApplication app) => app;
}
