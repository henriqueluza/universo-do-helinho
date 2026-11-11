using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class ApiLoginTests(PostgresFixture postgres)
{
    private const string Password = "Senha-Sintetica-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _lines.Enqueue(formatter(state, exception) + exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    _lines.Enqueue($"{pair.Key}={pair.Value}");
                }
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed record Account(string Enrollment, string UserName, string Email, Guid UserId, Guid StudentId);

    private async Task<(WebApplicationFactory<Program> Factory, FakeClock Clock)> CreateFactoryAsync(
        int rateLimit = 1000, ILoggerProvider? logs = null, string? tokenLifetime = null)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var factory = new HelinhoWebFactory(postgres).WithWebHostBuilder(b =>
        {
            b.UseSetting("Api:Auth:LoginRateLimit", rateLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (tokenLifetime is not null)
            {
                b.UseSetting("Api:Auth:TokenLifetime", tokenLifetime);
            }

            b.ConfigureServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<IClock>(clock));
                if (logs is not null)
                {
                    s.AddSingleton(logs);
                }
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(Ct);
        return (factory, clock);
    }

    private static async Task<Account> CreateUserAsync(
        IServiceProvider services, string role, bool approved = true, string password = Password)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(role));
        }

        var tag = Guid.NewGuid().ToString("N")[..10];
        var user = new AppUser { UserName = $"u{tag}", Email = $"u{tag}@exemplo.test" };
        Assert.True((await sp.GetRequiredService<UserManager<AppUser>>().CreateAsync(user, password)).Succeeded);
        await sp.GetRequiredService<UserManager<AppUser>>().AddToRoleAsync(user, role);

        var studentId = Guid.Empty;
        if (role == RoleNames.Student)
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var unit = new Unit($"Unidade {tag}");
            db.Units.Add(unit);
            var student = new Student(
                user.Id, unit.Id, $"M{tag}", "Aluno Sintético", $"rg{tag}", $"cpf{tag}", "Rua Fictícia, 1",
                new DateOnly(2016, 5, 5), "Responsável Sintético", "5A", new DateOnly(2026, 11, 10));
            if (approved)
            {
                student.ApproveByGuardian();
            }

            db.Students.Add(student);
            await db.SaveChangesAsync(Ct);
            studentId = student.Id;
        }

        return new Account($"M{tag}", user.UserName, user.Email, user.Id, studentId);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string login, string password) =>
        client.PostAsJsonAsync("/api/login", new { login, password }, Ct);

    [Theory]
    [InlineData("enrollment")]
    [InlineData("username")]
    [InlineData("email")]
    public async Task Login_por_matricula_usuario_ou_email_devolve_token_no_contrato(string kind)
    {
        var (factory, clock) = await CreateFactoryAsync();
        await using var _ = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        var login = kind switch { "enrollment" => account.Enrollment, "username" => account.UserName, _ => account.Email };
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, login, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["token", "expires_at", "user_id"], body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.All(body.EnumerateObject(), p => Assert.Equal(JsonValueKind.String, p.Value.ValueKind));
        Assert.Equal(account.StudentId.ToString(), body.GetProperty("user_id").GetString());
        var expiresAt = body.GetProperty("expires_at").GetString()!;
        Assert.EndsWith("Z", expiresAt, StringComparison.Ordinal);
        Assert.InRange(
            DateTimeOffset.Parse(expiresAt, System.Globalization.CultureInfo.InvariantCulture) - clock.UtcNow, TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1), TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task Banco_guarda_so_o_hash_e_o_token_autentica_ate_vencer()
    {
        var (factory, clock) = await CreateFactoryAsync();
        await using var _ = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        using var client = factory.CreateClient();
        var token = (await (await LoginAsync(client, account.Enrollment, Password))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ApiSessions
                .Where(s => s.UserId == account.UserId).ToListAsync(Ct);
            var session = Assert.Single(stored);
            Assert.NotEqual(token, session.TokenHash);
            Assert.DoesNotContain(token, session.TokenHash, StringComparison.Ordinal);
        }

        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new("Bearer", token);
        // 501 = autenticou e chegou à rota ainda sem lógica.
        Assert.Equal(HttpStatusCode.NotImplemented, (await authed.GetAsync("/api/user/badges?page=1", Ct)).StatusCode);

        clock.UtcNow = clock.UtcNow.AddHours(8).AddSeconds(1);
        var expired = await authed.GetAsync("/api/user/stats", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal("token_expired", (await expired.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_desconhecido_responde_401_token_expired()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "desconhecido");

        var response = await client.GetAsync("/api/user/stats", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("token_expired", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Senha_errada_e_usuario_inexistente_respondem_o_mesmo_401()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        using var client = factory.CreateClient();

        var wrongPassword = await LoginAsync(client, account.Enrollment, "senha-errada");
        var unknownUser = await LoginAsync(client, "nao-existe", "senha-errada");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        var expected = await wrongPassword.Content.ReadAsStringAsync(Ct);
        Assert.Equal(expected, await unknownUser.Content.ReadAsStringAsync(Ct));
        Assert.Contains("invalid_credentials", expected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Usuario_inexistente_paga_o_custo_da_verificacao_de_senha()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        using var client = factory.CreateClient();
        await LoginAsync(client, account.Enrollment, "aquecimento");

        var known = await MedianMillisecondsAsync(() => LoginAsync(client, account.Enrollment, "errada-1"), 5);
        var unknown = await MedianMillisecondsAsync(() => LoginAsync(client, $"x{Guid.NewGuid():N}", "errada-1"), 5);

        // Sem o hash descartável o usuário inexistente seria ordens de grandeza mais rápido.
        Assert.True(unknown > known / 3, $"desconhecido {unknown} ms vs conhecido {known} ms");
    }

    private static async Task<double> MedianMillisecondsAsync(Func<Task<HttpResponseMessage>> call, int runs)
    {
        var times = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await call();
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        times.Sort();
        return times[runs / 2];
    }

    [Fact]
    public async Task Aluno_sem_aprovacao_administrador_e_master_respondem_403_forbidden()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var pending = await CreateUserAsync(factory.Services, RoleNames.Student, approved: false);
        var admin = await CreateUserAsync(factory.Services, RoleNames.Admin);
        var master = await CreateUserAsync(factory.Services, RoleNames.Master);
        using var client = factory.CreateClient();

        foreach (var login in new[] { pending.Enrollment, admin.UserName, master.Email })
        {
            var response = await LoginAsync(client, login, Password);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("forbidden", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Senha_errada_de_administrador_continua_401()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var admin = await CreateUserAsync(factory.Services, RoleNames.Admin);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, admin.UserName, "errada")).StatusCode);
    }

    [Fact]
    public async Task Corpo_vazio_ou_invalido_responde_401_invalid_credentials()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        foreach (var payload in new object[] { new { }, new { login = "", password = "" }, new { login = new string('a', 1000), password = "x" } })
        {
            var response = await client.PostAsJsonAsync("/api/login", payload, Ct);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Bloqueio_por_tentativas_nega_ate_a_senha_certa()
    {
        var (factory, _) = await CreateFactoryAsync();
        await using var _f = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, account.Enrollment, "errada")).StatusCode);
        }

        var response = await LoginAsync(client, account.Enrollment, Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_credentials", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limite_de_taxa_do_login_responde_429_depois_do_limite()
    {
        var (factory, _) = await CreateFactoryAsync(rateLimit: 3);
        await using var _f = factory;
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "x", "y")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "x", "y")).StatusCode);
    }

    [Fact]
    public async Task Validade_do_token_e_configuravel_por_options()
    {
        var (factory, clock) = await CreateFactoryAsync(tokenLifetime: "00:30:00");
        await using var _f = factory;
        var account = await CreateUserAsync(factory.Services, RoleNames.Student);
        using var client = factory.CreateClient();

        var body = await (await LoginAsync(client, account.Enrollment, Password)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var lifetime = DateTimeOffset.Parse(body.GetProperty("expires_at").GetString()!, System.Globalization.CultureInfo.InvariantCulture) - clock.UtcNow;
        Assert.InRange(lifetime, TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Token_e_senha_nunca_aparecem_em_log()
    {
        var logs = new CapturingLoggerProvider();
        var (factory, clock) = await CreateFactoryAsync(logs: logs);
        await using var _f = factory;
        var student = await CreateUserAsync(factory.Services, RoleNames.Student);
        var pending = await CreateUserAsync(factory.Services, RoleNames.Student, approved: false, password: "Pendente-Sintetica-2");
        using var client = factory.CreateClient();

        var ok = await LoginAsync(client, student.Enrollment, Password);
        var token = (await ok.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
        await LoginAsync(client, student.Enrollment, "Errada-Sintetica-3");
        await LoginAsync(client, "inexistente", "Desconhecida-Sintetica-4");
        await LoginAsync(client, pending.Enrollment, "Pendente-Sintetica-2");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await authed.GetAsync("/api/user/stats", Ct);
        clock.UtcNow = clock.UtcNow.AddHours(9);
        await authed.GetAsync("/api/user/stats", Ct);

        Assert.NotEmpty(logs.Lines);
        foreach (var secret in new[] { token, Password, "Errada-Sintetica-3", "Desconhecida-Sintetica-4", "Pendente-Sintetica-2" })
        {
            Assert.DoesNotContain(logs.Lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }
}
