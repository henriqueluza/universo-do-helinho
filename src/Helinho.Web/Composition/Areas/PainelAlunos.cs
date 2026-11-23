using Helinho.Infrastructure.DependencyInjection;

namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área PainelAlunos: só a Issue dessa área edita este arquivo.</summary>
public static class PainelAlunosArea
{
    public static IServiceCollection AddPainelAlunos(this IServiceCollection services) => services.AddStudentAccess();

    public static WebApplication MapPainelAlunos(this WebApplication app) => app;
}
