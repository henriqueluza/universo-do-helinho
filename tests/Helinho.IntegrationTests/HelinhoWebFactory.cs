using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Helinho.IntegrationTests;

/// <summary>WebApplicationFactory apontando para o PostgreSQL do container.</summary>
public sealed class HelinhoWebFactory(PostgresFixture postgres, string? seedPassword = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
        if (seedPassword is not null)
        {
            builder.UseSetting("HELINHO_SEED_PASSWORD", seedPassword);
        }
    }
}
