using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Helinho.Web.Api;
using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class ApiContractEndpointsTests(PostgresFixture postgres)
{
    private static readonly (string Method, string Path)[] Routes =
    [
        ("POST", "/api/login"), ("POST", "/api/logout"), ("GET", "/api/profile"), ("GET", "/api/live-event"),
        ("GET", "/api/quizzes/progress"), ("GET", "/api/quiz/random"), ("POST", "/api/quiz/{question_id}/answer"),
        ("GET", "/api/quests/daily"), ("GET", "/api/quests/daily-bonus"), ("GET", "/api/user/stats"),
        ("GET", "/api/user/badges"), ("GET", "/api/user/inventory"), ("POST", "/api/user/inventory"),
        ("POST", "/api/devices/push-token"),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rotas_autenticadas_respondem_501_com_corpo_de_erro_padrao()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        var calls = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Post, "/api/logout"),
            (HttpMethod.Post, "/api/quiz/q-1/answer"), (HttpMethod.Get, "/api/user/stats"),
            (HttpMethod.Get, "/api/user/badges?page=1"), (HttpMethod.Get, "/api/user/inventory"),
            (HttpMethod.Post, "/api/user/inventory"), (HttpMethod.Post, "/api/devices/push-token"),
            (HttpMethod.Post, "/api/login"),
        };
        Assert.Equal(Routes.Length - 6, calls.Length); // /api/profile, /live-event, /quizzes/progress, /quiz/random e /quests/* já têm lógica (testes próprios)

        foreach (var (method, path) in calls)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
            {
                request.Content = JsonContent.Create(new { });
            }

            var response = await client.SendAsync(request, Ct);

            Assert.True(response.StatusCode == HttpStatusCode.NotImplemented, $"{method} {path}: {response.StatusCode}");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("not_implemented", body.GetProperty("error").GetString());
            Assert.True(body.TryGetProperty("message", out _));
        }
    }

    [Fact]
    public async Task Sem_token_ou_com_token_invalido_responde_401_token_expired()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var anonymous = factory.CreateClient();
        using var invalid = factory.CreateClient();
        invalid.DefaultRequestHeaders.Authorization = new("Bearer", "qualquer-coisa");

        foreach (var client in new[] { anonymous, invalid })
        {
            var response = await client.GetAsync("/api/profile", Ct);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("token_expired", body.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Openapi_descreve_as_14_rotas_e_scalar_abre()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var client = factory.CreateClient();

        var doc = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        var operations = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => (m.Name.ToUpperInvariant(), p.Name)))
            .ToHashSet();
        foreach (var route in Routes)
        {
            Assert.Contains(route, operations);
        }

        var scalar = await client.GetAsync("/scalar", Ct);
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
    }

    [Fact]
    public async Task Login_aceita_chamada_anonima()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/login", new { }, Ct);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public void Missao_generica_e_erro_invalid_credentials_seguem_o_contrato()
    {
        var options = ApiJson.CreateOptions();
        var quest = JsonSerializer.SerializeToElement(new QuestItem("q1", "Missão", 0, 3, 10, string.Empty), options);
        var error = JsonSerializer.SerializeToElement(ApiErrors.CreateInvalidCredentials(), options);

        Assert.Equal(JsonValueKind.String, quest.GetProperty("quiz_type").ValueKind);
        Assert.Equal(string.Empty, quest.GetProperty("quiz_type").GetString());
        Assert.Equal("invalid_credentials", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Handler_responde_403_forbidden_com_corpo_padrao()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IApiSessionValidator, TestSessionValidator>();
        builder.Services.AddApiContracts();
        builder.Services.AddAuthorizationBuilder().AddPolicy(
            "NegaTudo", p => p.AddAuthenticationSchemes(ApiAuth.Scheme).RequireAssertion(_ => false));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/proibido", () => "x").RequireAuthorization("NegaTudo");
        await app.StartAsync(Ct);
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", TestSessionValidator.Prefix + Guid.NewGuid());

        var response = await http.GetAsync("/proibido", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("forbidden", body.GetProperty("error").GetString());
    }

    [Fact]
    public void ICurrentStudent_le_o_aluno_do_principal_e_lanca_sem_autenticacao()
    {
        var id = Guid.NewGuid();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test");
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        Assert.Equal(id, new HttpContextCurrentStudent(accessor).StudentId);

        accessor.HttpContext = new DefaultHttpContext();
        Assert.Throws<InvalidOperationException>(() => new HttpContextCurrentStudent(accessor).StudentId);
    }
}
