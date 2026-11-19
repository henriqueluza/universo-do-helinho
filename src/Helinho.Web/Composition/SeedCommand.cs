using Helinho.Infrastructure.Seeding;

namespace Helinho.Web.Composition;

public static class SeedCommand
{
    /// <summary>
    /// `dotnet run --project src/Helinho.Web -- migrate|seed`: executa o comando de banco e encerra.
    /// O código de saída vai em <see cref="Environment.ExitCode"/> (o Program.cs só faz <c>return</c>).
    /// </summary>
    public static async Task<bool> TryRunSeedCommandAsync(this WebApplication app, string[] args)
    {
        var exitCode = await RunDatabaseCommandAsync(app.Services, args, Console.Error, CancellationToken.None);
        if (exitCode is null)
        {
            return false;
        }

        Environment.ExitCode = exitCode.Value;
        return true;
    }

    /// <summary>
    /// <c>migrate</c>: aplica as migrações e, com <c>Seed:Enabled=true</c>, roda o seed. <c>seed</c>: só roda com a flag ligada.
    /// Devolve o código de saída, ou <c>null</c> quando <paramref name="args"/> não traz comando de banco.
    /// </summary>
    public static async Task<int?> RunDatabaseCommandAsync(
        IServiceProvider services, string[] args, TextWriter error, CancellationToken cancellationToken)
    {
        var migrate = args.Contains("migrate", StringComparer.Ordinal);
        if (!migrate && !args.Contains("seed", StringComparer.Ordinal))
        {
            return null;
        }

        await using var scope = services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<DatabaseCommands>();
        if (migrate)
        {
            await commands.MigrateAsync(cancellationToken);
            return 0;
        }

        if (await commands.TrySeedAsync(cancellationToken))
        {
            return 0;
        }

        await error.WriteLineAsync(
            "ERRO: seed recusado. O seed sintético só roda com Seed:Enabled=true (variável Seed__Enabled=true); nada foi gravado.");
        return 1;
    }
}
