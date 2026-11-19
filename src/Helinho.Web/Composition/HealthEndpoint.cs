using Helinho.Application.Abstractions;

namespace Helinho.Web.Composition;

public static class HealthEndpoint
{
    /// <summary>Teto da consulta ao banco em <c>/health/ready</c>: o healthcheck não pode ficar pendurado.</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(3);

    public static WebApplication MapHealth(this WebApplication app)
    {
        // Liveness: só o processo, sem dependências.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        // Readiness: o banco responde? O corpo nunca leva detalhe da falha (exceção, connection string, host).
        app.MapGet("/health/ready", async (IServiceScopeFactory scopes, CancellationToken cancellationToken) =>
            await CanReachDatabaseAsync(scopes, cancellationToken)
                ? Results.Ok(new { status = "ok" })
                : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        return app;
    }

    private static async Task<bool> CanReachDatabaseAsync(IServiceScopeFactory scopes, CancellationToken requestAborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(ReadyTimeout);
        try
        {
            // WaitAsync garante o teto: com servidor que aceita o TCP e não responde, só o token não basta (a tentativa dura o Timeout da connection string).
            return await ProbeAsync(scopes, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false; // tempo esgotado ou cliente desistiu: não está pronto
        }
    }

    /// <summary>Escopo próprio: se a rota desistir de esperar, o DbContext só é descartado quando a tentativa terminar.</summary>
    private static async Task<bool> ProbeAsync(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            // CanConnectAsync devolve false para falha de conexão; cancelamento tratado à parte para a tarefa abandonada não falhar.
            return await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Database.CanConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
