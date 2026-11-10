namespace Helinho.Web.Composition;

public static class HealthEndpoint
{
    public static WebApplication MapHealth(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        return app;
    }
}
