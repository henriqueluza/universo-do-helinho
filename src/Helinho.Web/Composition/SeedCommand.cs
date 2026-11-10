using Helinho.Infrastructure.Seeding;

namespace Helinho.Web.Composition;

public static class SeedCommand
{
    /// <summary>`dotnet run --project src/Helinho.Web -- seed`: migra o banco, roda os ISeeder e encerra.</summary>
    public static async Task<bool> TryRunSeedCommandAsync(this WebApplication app, string[] args)
    {
        if (!args.Contains("seed", StringComparer.Ordinal))
        {
            return false;
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(CancellationToken.None);
        return true;
    }
}
