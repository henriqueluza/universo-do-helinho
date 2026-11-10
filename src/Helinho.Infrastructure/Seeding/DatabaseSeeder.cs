using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Seeding;

/// <summary>Aplica as migrações e executa todo <see cref="ISeeder"/> registrado, em ordem.</summary>
public sealed class DatabaseSeeder(AppDbContext db, IEnumerable<ISeeder> seeders)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
