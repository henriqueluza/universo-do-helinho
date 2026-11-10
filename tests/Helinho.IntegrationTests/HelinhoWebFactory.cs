using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Helinho.IntegrationTests;

/// <summary>WebApplicationFactory apontando para o PostgreSQL do container.</summary>
public sealed class HelinhoWebFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
    }
}
