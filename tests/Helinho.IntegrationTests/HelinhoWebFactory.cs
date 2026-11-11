using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Helinho.IntegrationTests;

/// <summary>WebApplicationFactory apontando para o PostgreSQL do container.</summary>
public sealed class HelinhoWebFactory(PostgresFixture postgres, string? seedPassword = null, IClock? clock = null) : WebApplicationFactory<Program>
{
    // Banco próprio por fábrica (o migrate o cria): o seed de conteúdo não vaza questões para testes que contam as suas.
    private readonly string _connectionString =
        new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = $"t_{Guid.NewGuid():N}" }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        // Sem senha explícita, usa uma aleatória por fábrica (o seed exige a variável); "" simula a ausência.
        builder.UseSetting("HELINHO_SEED_PASSWORD", seedPassword ?? $"{Guid.NewGuid():N}Aa1!");

        builder.ConfigureServices(services =>
        {
            if (clock is not null)
            {
                services.AddSingleton(clock); // substitui o IClock registrado (último vence)
            }

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
