using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Helinho.IntegrationTests;

/// <summary>Issue #215: a suíte não pode consumir instâncias de inotify (limite de 128 por usuário no runner do CI).</summary>
[Collection(PostgresCollection.Name)]
public class ObservadoresDeArquivoTests(PostgresFixture postgres)
{
    private const int Hosts = 5;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Instâncias de inotify abertas por este processo (só existe no Linux; nos demais sistemas, zero).</summary>
    private static int InstanciasDeInotify() =>
        !OperatingSystem.IsLinux()
            ? 0
            : Directory.EnumerateFileSystemEntries("/proc/self/fd").Count(fd =>
            {
                try
                {
                    return new FileInfo(fd).LinkTarget?.Contains("inotify", StringComparison.Ordinal) == true;
                }
                catch (IOException)
                {
                    return false; // descritor fechado entre a listagem e a leitura
                }
            });

    private static IEnumerable<PhysicalFileProvider> Fisicos(IFileProvider provider) => provider switch
    {
        PhysicalFileProvider physical => [physical],
        CompositeFileProvider composite => composite.FileProviders.SelectMany(Fisicos),
        _ => [],
    };

    [Fact]
    public async Task Hosts_de_teste_que_renderizam_o_layout_nao_abrem_instancias_de_inotify()
    {
        var antes = InstanciasDeInotify();
        var fabricas = new List<HelinhoWebFactory>();
        try
        {
            for (var i = 0; i < Hosts; i++)
            {
                var factory = new HelinhoWebFactory(postgres);
                fabricas.Add(factory);
                await factory.EnsureDatabaseAsync();
                using var client = factory.CreateAnonymousClient();

                // /entrar renderiza o layout com asp-append-version, que observa os arquivos do wwwroot.
                using var response = await client.GetAsync("/entrar", Ct);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("?v=", await response.Content.ReadAsStringAsync(Ct));
            }

            // Com os hosts ainda vivos: nenhum observador do sistema operacional, nem dos appsettings nem do wwwroot.
            Assert.Equal(antes, InstanciasDeInotify());
        }
        finally
        {
            foreach (var factory in fabricas)
            {
                await factory.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Host_de_teste_nao_recarrega_configuracao_e_observa_arquivos_por_sondagem()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var configuration = Assert.IsAssignableFrom<IConfigurationRoot>(factory.Services.GetRequiredService<IConfiguration>());

        var arquivos = configuration.Providers.OfType<FileConfigurationProvider>().ToList();
        Assert.NotEmpty(arquivos); // os appsettings continuam sendo lidos
        Assert.All(arquivos, p => Assert.False(p.Source.ReloadOnChange));

        var fisicos = Fisicos(environment.WebRootFileProvider).Concat(Fisicos(environment.ContentRootFileProvider)).ToList();
        Assert.NotEmpty(fisicos);
        Assert.All(fisicos, p => Assert.True(p.UseActivePolling));
    }
}
