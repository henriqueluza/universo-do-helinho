using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Chaves do Data Protection no PostgreSQL (ADR-0002 §9): o que uma instância protege, a seguinte lê.</summary>
[Collection(PostgresCollection.Name)]
public class DataProtectionKeysTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CaptureLogger : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }

    private async Task<HelinhoWebFactory> NewFactoryAsync(bool realCookieAuth = false, Action<IServiceCollection>? configure = null)
    {
        var factory = new HelinhoWebFactory(postgres, Password, realCookieAuth: realCookieAuth, configure: configure);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    /// <summary>Simula um redeploy: outro host, com contêiner de serviços e anel de chaves novos, no mesmo banco.</summary>
    private static WebApplicationFactory<Program> Redeploy(HelinhoWebFactory factory) => factory.WithWebHostBuilder(_ => { });

    private static IDataProtector Protector(IServiceProvider services) =>
        services.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste-sintetico");

    private static async Task<List<string>> StoredKeysAsync(HelinhoWebFactory factory, bool delete = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keys = await db.DataProtectionKeys.Select(k => k.Xml ?? string.Empty).ToListAsync(Ct);
        if (delete)
        {
            await db.DataProtectionKeys.ExecuteDeleteAsync(Ct);
        }

        return keys;
    }

    [Fact]
    public async Task Valor_protegido_por_uma_instancia_e_lido_por_outra_no_mesmo_banco()
    {
        await using var factory = await NewFactoryAsync();
        var payload = Protector(factory.Services).Protect("valor-sintetico");

        await using var redeploy = Redeploy(factory);
        Assert.Equal("valor-sintetico", Protector(redeploy.Services).Unprotect(payload));
        Assert.Equal("universo-do-helinho", redeploy.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.NotEmpty(await StoredKeysAsync(factory, delete: true));

        // Controle negativo: sem as linhas da tabela, uma instância nova não lê o valor — as chaves vêm só do banco.
        await using var withoutKeys = Redeploy(factory);
        Assert.Throws<CryptographicException>(() => Protector(withoutKeys.Services).Unprotect(payload));
    }

    /// <summary>
    /// O <c>migrate</c> do pre-deploy vem antes de a aplicação atender (ADR-0002 §7.3): sem a tabela, nada é protegido,
    /// porque não existe chave fora do banco; aplicada a migração, o mesmo host volta a proteger sem reiniciar.
    /// </summary>
    [Fact]
    public async Task Sem_o_esquema_a_protecao_falha_e_volta_a_funcionar_depois_do_migrate_sem_reiniciar()
    {
        await using var factory = new HelinhoWebFactory(postgres, Password);
        var protector = Protector(factory.Services);

        Assert.Throws<CryptographicException>(() => protector.Protect("valor-sintetico"));

        await factory.EnsureDatabaseAsync();
        Assert.Equal("valor-sintetico", protector.Unprotect(protector.Protect("valor-sintetico")));
        Assert.NotEmpty(await StoredKeysAsync(factory));
    }

    /// <summary>A conexão que o anel de chaves abre de forma síncrona não negocia GSS (ver <c>WithoutGssNegotiation</c>).</summary>
    [Fact]
    public async Task Conexao_registrada_no_host_nao_negocia_GSS()
    {
        await using var factory = new HelinhoWebFactory(postgres, Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();

        Assert.Equal(Npgsql.GssEncryptionMode.Disable, new Npgsql.NpgsqlConnectionStringBuilder(connectionString).GssEncryptionMode);
    }

    [Fact]
    public async Task Cookie_de_login_emitido_por_uma_instancia_e_aceito_por_outra_no_mesmo_banco()
    {
        await using var factory = await NewFactoryAsync(realCookieAuth: true);
        using var client = factory.CreateAnonymousClient();
        var html = await client.GetStringAsync("/entrar", Ct);
        var login = await client.PostAsync("/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
            ["Input.UserName"] = "master",
            ["Input.Password"] = Password,
        }), Ct);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("helinho.panel=", StringComparison.Ordinal)).Split(';')[0];

        static async Task<HttpResponseMessage> DashboardAsync(WebApplicationFactory<Program> app, string cookie)
        {
            using var other = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
            other.DefaultRequestHeaders.Add("Cookie", cookie);
            return await other.GetAsync("/Dashboard", Ct);
        }

        await using var redeploy = Redeploy(factory);
        var accepted = await DashboardAsync(redeploy, cookie);
        await StoredKeysAsync(factory, delete: true);
        await using var withoutKeys = Redeploy(factory);
        var rejected = await DashboardAsync(withoutKeys, cookie); // controle negativo: sem as chaves do banco, o cookie não vale

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains(">Master<", await accepted.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
    }

    [Fact]
    public async Task Material_da_chave_nao_vai_para_o_log_mesmo_com_o_Data_Protection_em_Trace()
    {
        var logs = new CaptureLogger();
        await using var factory = await NewFactoryAsync(configure: s => s.AddLogging(b =>
            b.AddProvider(logs).AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Trace)));
        Protector(factory.Services).Protect("valor-sintetico");
        await using var redeploy = Redeploy(factory);
        Protector(redeploy.Services).Protect("valor-sintetico"); // a instância nova lê a chave gravada pela primeira

        var secrets = (await StoredKeysAsync(factory)).Select(xml => XElement.Parse(xml).Descendants("value").Single().Value).ToList();
        var lines = logs.Lines.ToArray();
        Assert.NotEmpty(secrets);
        Assert.Contains(lines, l => l.Contains("Creating key", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("masterKey", StringComparison.Ordinal) || secrets.Any(s => l.Contains(s, StringComparison.Ordinal)));
    }
}
