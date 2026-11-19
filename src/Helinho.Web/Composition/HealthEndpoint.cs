using Helinho.Application.Abstractions;

namespace Helinho.Web.Composition;

public static partial class HealthEndpoint
{
    /// <summary>Teto da consulta ao banco em <c>/health/ready</c>: o healthcheck não pode ficar pendurado.</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(3);

    public static WebApplication MapHealth(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        // Readiness: o banco responde? O corpo nunca leva detalhe da falha (exceção, connection string, host).
        app.MapGet("/health/ready", async (IServiceScopeFactory scopes, CancellationToken cancellationToken) =>
            await CanReachDatabaseAsync(scopes, app.Logger, cancellationToken)
                ? Results.Ok(new { status = "ok" })
                : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        return app;
    }

    private static async Task<bool> CanReachDatabaseAsync(IServiceScopeFactory scopes, ILogger logger, CancellationToken requestAborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(ReadyTimeout);
        try
        {
            // WaitAsync garante o teto: com servidor que aceita o TCP e não responde, só o token não basta (a tentativa dura o Timeout da connection string).
            return await ProbeAsync(scopes, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            // Tempo esgotado, cliente desistiu ou falha inesperada: não está pronto. Só o tipo vai para o log (a mensagem pode citar o host).
            LogProbeFailed(logger, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Escopo próprio: se a rota desistir de esperar, o DbContext só é descartado quando a tentativa (já abandonada, por isso sem propagar o cancelamento) terminar.</summary>
    private static async Task<bool> ProbeAsync(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Database.CanConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sondagem de /health/ready sem sucesso: {Tipo}")]
    private static partial void LogProbeFailed(ILogger logger, string tipo);
}
