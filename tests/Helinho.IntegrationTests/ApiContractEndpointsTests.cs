using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
            (HttpMethod.Post, "/api/logout"), (HttpMethod.Get, "/api/profile"), (HttpMethod.Get, "/api/live-event"),
            (HttpMethod.Get, "/api/quizzes/progress"), (HttpMethod.Get, "/api/quiz/random?quiz_type=ambiental"),
            (HttpMethod.Post, "/api/quiz/q-1/answer"), (HttpMethod.Get, "/api/quests/daily"),
            (HttpMethod.Get, "/api/quests/daily-bonus"), (HttpMethod.Get, "/api/user/stats"),
            (HttpMethod.Get, "/api/user/badges?page=1"), (HttpMethod.Get, "/api/user/inventory"),
            (HttpMethod.Post, "/api/user/inventory"), (HttpMethod.Post, "/api/devices/push-token"),
            (HttpMethod.Post, "/api/login"),
        };
        Assert.Equal(Routes.Length, calls.Length);

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
}
