using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Issue #98: JSON no console fora de Development, sem SQL e sem credencial em log (ADR-0001 §7, T4).</summary>
[Collection(PostgresCollection.Name)]
public class LoggingTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";
    private const string WrongPassword = "Errada-Sintetica-9";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Guarda mensagem, exceção, estado estruturado e escopos de tudo o que passa pelos filtros de nível.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            Capture(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Enqueue(formatter(state, exception) + exception);
            Capture(state);
        }

        public void Dispose()
        {
        }

        private void Capture<TState>(TState state)
        {
            Lines.Enqueue($"{state}");
            foreach (var pair in state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            {
                Lines.Enqueue($"{pair.Key}={pair.Value}");
            }
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string environment, Action<IServiceCollection>? configure = null) =>
        new HelinhoWebFactory(postgres, realCookieAuth: true, configure: configure).WithWebHostBuilder(b => b.UseEnvironment(environment));

    [Fact]
    public async Task Em_Production_o_console_sai_em_JSON_de_uma_linha_com_timestamp_UTC_e_sem_SQL()
    {
        await using var factory = CreateFactory(Environments.Production);
        var services = factory.Services;
        var console = services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value;
        var json = services.GetRequiredService<IOptions<JsonConsoleFormatterOptions>>().Value;
        var loggers = services.GetRequiredService<ILoggerFactory>();

        Assert.True(services.GetRequiredService<IHostEnvironment>().IsProduction());
        Assert.Equal(ConsoleFormatterNames.Json, console.FormatterName);
        Assert.True(json.UseUtcTimestamp);
        Assert.False(json.IncludeScopes);
        foreach (var category in new[] { "Microsoft.EntityFrameworkCore.Database.Command", "Microsoft.AspNetCore.Hosting.Diagnostics" })
        {
            Assert.False(loggers.CreateLogger(category).IsEnabled(LogLevel.Information));
            Assert.True(loggers.CreateLogger(category).IsEnabled(LogLevel.Warning));
        }

        // A saída real do formatador configurado: um objeto JSON por linha, mesmo com quebra de linha na mensagem.
        var formatter = services.GetServices<ConsoleFormatter>().Single(f => f.Name == console.FormatterName);
        using var writer = new StringWriter();
        formatter.Write(
            new LogEntry<string>(LogLevel.Information, "Categoria", new EventId(1), "linha 1\nlinha 2", null, (state, _) => state),
            scopeProvider: null,
            writer);
        var line = writer.ToString().TrimEnd('\r', '\n');
        Assert.DoesNotContain('\n', line);
        using var document = JsonDocument.Parse(line);
        var timestamp = document.RootElement.GetProperty("Timestamp");
        Assert.EndsWith("Z", timestamp.GetString(), StringComparison.Ordinal);
        Assert.InRange(timestamp.GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal("linha 1\nlinha 2", document.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task Em_Development_o_console_continua_no_formato_legivel_sem_timestamp()
    {
        await using var factory = CreateFactory(Environments.Development);

        Assert.Equal(
            ConsoleFormatterNames.Simple, factory.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        Assert.Null(factory.Services.GetRequiredService<IOptions<SimpleConsoleFormatterOptions>>().Value.TimestampFormat);
    }

    /// <summary>Com os níveis efetivos de Production e, como pior caso, com tudo em Trace só para o provedor de captura.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_com_senha_errada_no_painel_e_na_API_nao_deixa_identificador_senha_nem_token_no_log(bool trace)
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(Environments.Production, services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            if (trace)
            {
                services.AddLogging(logging => logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace));
            }
        });
        var tag = Guid.NewGuid().ToString("N")[..10];
        var (admin, student, enrollment, rg, cpf) = ($"adm{tag}", $"alu{tag}", $"M{tag}", $"rg{tag}", $"cpf{tag}");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<DatabaseSeeder>().RunAsync(Ct);
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var db = sp.GetRequiredService<AppDbContext>();
            var unit = new Unit($"Unidade {tag}");
            db.Units.Add(unit);
            foreach (var (name, role) in new[] { (admin, RoleNames.Admin), (student, RoleNames.Student) })
            {
                var user = new AppUser { UserName = name, Email = $"{name}@exemplo.test" };
                Assert.True((await users.CreateAsync(user, Password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
                if (role == RoleNames.Student)
                {
                    var record = new Student(
                        user.Id, unit.Id, enrollment, "Aluno Sintético", rg, cpf, "Rua Fictícia, 1",
                        new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 10));
                    record.ApproveByGuardian();
                    db.Students.Add(record);
                }
            }

            await db.SaveChangesAsync(Ct);
        }

        // Fora de Development, HTTP puro é redirecionado para HTTPS (#97): o cliente já fala HTTPS.
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var form = await (await client.GetAsync("/entrar", Ct)).Content.ReadAsStringAsync(Ct);
        var antiforgery = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var panel = await client.PostAsync(
            "/entrar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgery,
                ["Input.UserName"] = admin,
                ["Input.Password"] = WrongPassword,
            }),
            Ct);
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        foreach (var login in new[] { enrollment, student, $"{student}@exemplo.test" })
        {
            var denied = await client.PostAsJsonAsync("/api/login", new { login, password = WrongPassword }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        var ok = await client.PostAsJsonAsync("/api/login", new { login = enrollment, password = Password }, Ct);
        var token = (await ok.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString() ?? "";

        Assert.Contains(logs.Lines, line => line.Contains("/entrar", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, line => line.Contains("/api/login", StringComparison.Ordinal));
        foreach (var secret in new[] { admin, student, enrollment, rg, cpf, Password, WrongPassword, token, antiforgery })
        {
            Assert.NotEmpty(secret);
            Assert.DoesNotContain(logs.Lines, line => line.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }
}
