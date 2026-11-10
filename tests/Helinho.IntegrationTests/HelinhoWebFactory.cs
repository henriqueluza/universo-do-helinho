using Helinho.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>WebApplicationFactory apontando para o PostgreSQL do container.</summary>
public sealed class HelinhoWebFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
        builder.ConfigureServices(services =>
        {
            // Esquema de teste só autentica; desafio e acesso negado continuam no cookie real do painel.
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName);
        });
    }

    /// <summary>Cria o esquema do banco (e o seed sintético) antes de renderizar páginas que consultam dados.</summary>
    public async Task EnsureDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(CancellationToken.None);
    }
}
