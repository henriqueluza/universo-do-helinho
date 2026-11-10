using Helinho.Infrastructure.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class SeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Seeder_migra_e_executa_os_seeders_registrados()
    {
        await using var factory = new HelinhoWebFactory(postgres, $"{Guid.NewGuid():N}Aa1!");
        await using var scope = factory.Services.CreateAsyncScope();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        await seeder.RunAsync(TestContext.Current.CancellationToken);
    }
}
