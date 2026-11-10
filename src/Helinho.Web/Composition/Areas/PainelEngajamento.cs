namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área PainelEngajamento: só a Issue dessa área edita este arquivo.</summary>
public static class PainelEngajamentoArea
{
    public static IServiceCollection AddPainelEngajamento(this IServiceCollection services) => services;

    public static WebApplication MapPainelEngajamento(this WebApplication app) => app;
}
