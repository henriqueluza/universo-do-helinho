using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class HealthTests(PostgresFixture postgres)
{
    private const string Ok = """{"status":"ok"}""";
    private const string Unavailable = """{"status":"unavailable"}""";

    [Fact]
    public async Task Health_responde_200()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_responde_200_com_banco_e_sem_autenticacao()
    {
        // Cookie real do painel e nenhum cabeçalho de teste: a requisição é anônima de verdade.
        await using var factory = new HelinhoWebFactory(postgres, realCookieAuth: true);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Ok, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sem_banco_ready_responde_503_e_health_continua_200_ambos_anonimos()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // porta fechada: a conexão é recusada na hora
        await using var factory = new HelinhoWebFactory(postgres, realCookieAuth: true);
        using var client = ClientWithDatabaseAt(factory, port);

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        // Corpo exato: nada de exceção, connection string ou host.
        Assert.Equal(Unavailable, await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(Ok, await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Banco_que_nao_responde_ready_devolve_503_no_tempo_limite()
    {
        // Aceita a conexão TCP e nunca responde ao protocolo: só o tempo limite da rota encerra a consulta.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = ClientWithDatabaseAt(factory, ((IPEndPoint)listener.LocalEndpoint).Port, connectTimeoutSeconds: 60);

        var elapsed = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        elapsed.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(Unavailable, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, HealthEndpoint.ReadyTimeout + TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task Ready_respeita_o_cancelamento_da_requisicao_e_abandona_a_consulta_ao_banco()
    {
        // Banco mudo: aceita o TCP e nunca responde. Sem o token da requisição, a consulta só terminaria no teto de 3 s.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = ClientWithDatabaseAt(factory, ((IPEndPoint)listener.LocalEndpoint).Port, connectTimeoutSeconds: 60);
        var test = TestContext.Current.CancellationToken;

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(test);
        abort.CancelAfter(TimeSpan.FromMilliseconds(200));
        var elapsed = Stopwatch.StartNew();
        var clientCall = client.GetAsync("/health/ready", abort.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientCall);

        Assert.True(elapsed.Elapsed < HealthEndpoint.ReadyTimeout, "o cancelamento da requisição deveria encerrar a chamada antes do teto de 3 s");
    }

    /// <summary>Cliente de um host cuja connection string aponta para a porta dada em 127.0.0.1 (sem banco de verdade).</summary>
    private static HttpClient ClientWithDatabaseAt(HelinhoWebFactory factory, int port, int connectTimeoutSeconds = 2) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Default",
                $"Host=127.0.0.1;Port={port};Database=helinho;Username=helinho;Timeout={connectTimeoutSeconds};Pooling=false"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
