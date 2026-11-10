namespace Helinho.Web.Composition;

/// <summary>Ponto de extensão da área ApiQuiz: só a Issue dessa área edita este arquivo.</summary>
public static class ApiQuizArea
{
    public static IServiceCollection AddApiQuiz(this IServiceCollection services) => services;

    public static WebApplication MapApiQuiz(this WebApplication app) => app;
}
