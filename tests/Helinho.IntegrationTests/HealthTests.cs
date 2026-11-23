using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Helinho.Application.Abstractions;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class HealthTests(PostgresFixture postgres)
{
    private const string Ok = """{"status":"ok"}""";
    private const string Unavailable = """{"status":"unavailable"}""";

    /// <summary>
    /// Tempo limite de conexão dos testes de banco mudo: acima do teto da rota (<see cref="HealthEndpoint.ReadyTimeout"/>),
    /// para só a rota encerrar a consulta. Não é maior porque a subida do host também o espera: o anel de chaves do
    /// Data Protection é lido do banco, de forma síncrona, antes de o host atender (Issue #103).
    /// </summary>
    private const int MuteConnectTimeoutSeconds = 10;

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
        using var reserved = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reserved.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // reservada e sem Listen: conexão recusada na hora, sem corrida com outro processo
        await using var factory = new HelinhoWebFactory(postgres, realCookieAuth: true);
        using var client = ClientWithDatabaseAt(factory, Assert.IsType<IPEndPoint>(reserved.LocalEndPoint).Port);

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        var health = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(Unavailable, await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); // corpo exato: sem exceção, connection string ou host
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(Ok, await health.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Banco_que_nao_responde_ready_devolve_503_no_tempo_limite()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); // aceita a conexão TCP e nunca responde ao protocolo: só o tempo limite da rota encerra a consulta
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = ClientWithDatabaseAt(factory, ((IPEndPoint)listener.LocalEndpoint).Port, connectTimeoutSeconds: MuteConnectTimeoutSeconds);

        var elapsed = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(Unavailable, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, HealthEndpoint.ReadyTimeout + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Ready_encerra_a_consulta_quando_o_cliente_cancela_a_requisicao()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); // servidor mudo: sem o token da requisição, a rota só terminaria no tempo limite
        var served = new TaskCompletionSource();
        await using var factory = new HelinhoWebFactory(postgres, configure: s => s.AddSingleton<IStartupFilter>(new AfterRequest(() => served.TrySetResult())));
        using var client = ClientWithDatabaseAt(factory, ((IPEndPoint)listener.LocalEndpoint).Port, connectTimeoutSeconds: MuteConnectTimeoutSeconds);
        using var abort = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var request = client.GetAsync("/health/ready", abort.Token);
        // O critério é o servidor encerrar a rota bem antes do teto (senão, TimeoutException aqui); o que o cliente vê vem depois.
        await served.Task.WaitAsync(HealthEndpoint.ReadyTimeout - TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task Falha_inesperada_na_consulta_vira_503_sem_detalhe()
    {
        await using var factory = new HelinhoWebFactory(postgres, configure: s => s.AddScoped<IAppDbContext>(_ => throw new InvalidOperationException("Host=banco-interno")));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(Unavailable, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Cliente de um host cuja connection string aponta para a porta dada em 127.0.0.1 (sem banco de verdade).</summary>
    private static HttpClient ClientWithDatabaseAt(HelinhoWebFactory factory, int port, int connectTimeoutSeconds = 2) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(
                "ConnectionStrings:Default",
                $"Host=127.0.0.1;Port={port};Database=helinho;Username=helinho;Timeout={connectTimeoutSeconds};Pooling=false"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Avisa quando o servidor termina de atender uma requisição (o cliente não enxerga isso depois de cancelar).</summary>
    private sealed class AfterRequest(Action done) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => next(app.Use(async (context, following) =>
        {
            try { await following(context); }
            finally { done(); }
        }));
    }
}
