using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Infrastructure.Seeding;

/// <summary>Regra dos comandos de banco: migrar é sempre permitido; semear só com <see cref="SeedOptions.Enabled"/>.</summary>
public sealed class DatabaseCommands(AppDbContext db, DatabaseSeeder seeder, IOptions<SeedOptions> options)
{
    /// <summary>Aplica as migrações; com a flag ligada, roda o seed sintético em seguida.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await TrySeedAsync(cancellationToken);
    }

    /// <summary>Roda o seed sintético. Devolve <c>false</c>, sem tocar no banco, quando a flag está desligada.</summary>
    public async Task<bool> TrySeedAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return false;
        }

        await seeder.RunAsync(cancellationToken);
        return true;
    }
}
