using Helinho.Infrastructure.Persistence;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #95: comandos <c>migrate</c> e <c>seed</c> e a flag <c>Seed:Enabled</c>. Cada teste usa um banco próprio e vazio.</summary>
[Collection(PostgresCollection.Name)]
public class DatabaseCommandTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Host no ambiente dado; sem <paramref name="seedEnabled"/>, vale o padrão da configuração daquele ambiente.</summary>
    private static IServiceProvider Host(HelinhoWebFactory root, string environment, string? seedEnabled = null) =>
        root.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            if (seedEnabled is not null)
            {
                b.UseSetting("Seed:Enabled", seedEnabled);
            }
        }).Services;

    private static Task<int?> RunAsync(IServiceProvider services, string command, TextWriter? error = null) =>
        SeedCommand.RunDatabaseCommandAsync(services, [command], error ?? TextWriter.Null, Ct);

    /// <summary>Registros que o seed cria: usuários, unidades, questões e sessões de quiz (ignora-filtro: o teste não tem unidade de contexto).</summary>
    private static async Task<int[]> CountSeedRowsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        return
        [
            await db.Users.IgnoreQueryFilters().CountAsync(Ct), await db.Units.IgnoreQueryFilters().CountAsync(Ct),
            await db.Questions.IgnoreQueryFilters().CountAsync(Ct), await db.QuizSessions.IgnoreQueryFilters().CountAsync(Ct),
        ];
    }

    [Fact]
    public async Task Migrate_com_a_flag_desligada_por_padrao_aplica_as_migracoes_e_nao_semeia()
    {
        await using var root = new HelinhoWebFactory(postgres);
        var services = Host(root, "Production"); // sem Seed:Enabled: o padrão é desligado

        Assert.Equal(0, await RunAsync(services, "migrate"));

        Assert.All(await CountSeedRowsAsync(services), count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task Migrate_com_a_flag_ligada_semeia_e_e_idempotente()
    {
        await using var root = new HelinhoWebFactory(postgres);
        var services = Host(root, "Production", seedEnabled: "true");

        Assert.Equal(0, await RunAsync(services, "migrate"));
        var first = await CountSeedRowsAsync(services);
        Assert.Equal(0, await RunAsync(services, "migrate")); // segunda execução: não falha nem duplica

        Assert.All(first, count => Assert.True(count > 0));
        Assert.Equal(first, await CountSeedRowsAsync(services));
    }

    [Fact]
    public async Task Seed_com_a_flag_desligada_nao_grava_nada_e_sinaliza_erro()
    {
        await using var root = new HelinhoWebFactory(postgres);
        var services = Host(root, "Production", seedEnabled: "false");
        Assert.Equal(0, await RunAsync(services, "migrate")); // cria o esquema, para poder contar os registros
        using var error = new StringWriter();

        var exitCode = await RunAsync(services, "seed", error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Seed__Enabled", error.ToString(), StringComparison.Ordinal);
        Assert.All(await CountSeedRowsAsync(services), count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task Seed_em_Development_continua_migrando_e_semeando()
    {
        await using var root = new HelinhoWebFactory(postgres);
        var services = Host(root, "Development"); // appsettings.Development.json liga a flag: o fluxo local não muda

        Assert.Equal(0, await RunAsync(services, "seed"));

        Assert.All(await CountSeedRowsAsync(services), count => Assert.True(count > 0));
        Assert.Null(await RunAsync(services, "outro")); // sem comando de banco, o host segue a subida normal
    }
}
