namespace Helinho.Web.Composition;

/// <summary>Agrega as áreas. Cada área tem arquivo próprio em Composition/Areas/; Program.cs não muda.</summary>
public static class Areas
{
    public static IServiceCollection AddAreas(this IServiceCollection services)
    {
        services.AddHospedagem().AddIdentidade().AddApiHome().AddApiPerfil().AddApiQuiz()
            .AddPainelAlunos().AddPainelConteudo().AddPainelEngajamento().AddGestao();
        services.AddIa();
        return services;
    }

    public static WebApplication MapAreas(this WebApplication app)
    {
        app.MapIdentidade().MapApiHome().MapApiPerfil().MapApiQuiz()
            .MapPainelAlunos().MapPainelConteudo().MapPainelEngajamento().MapGestao();
        return app;
    }
}
