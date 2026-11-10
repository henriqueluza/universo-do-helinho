using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>GET /api/live-event: evento ativo, ausência (P5), expirado e desempate pelo que termina primeiro.</summary>
[Collection(PostgresCollection.Name)]
public class LiveEventTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Com_evento_ativo_responde_todos_os_campos_e_passa_pelo_verificador()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var endsAt = DateTimeOffset.UtcNow.AddHours(2);
        var id = await ResetAndSeedAsync(web, new LiveEvent("Festival do Conhecimento", "XP em dobro com o Byte", QuizType.Conhecimento, endsAt));
        using var client = web.CreateStudentClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/live-event", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(
            ["has_event", "id", "title", "subtitle", "quiz_type", "ends_at"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.True(body.GetProperty("has_event").GetBoolean());
        Assert.Equal(id.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("Festival do Conhecimento", body.GetProperty("title").GetString());
        Assert.Equal("XP em dobro com o Byte", body.GetProperty("subtitle").GetString());
        Assert.Equal("conhecimento", body.GetProperty("quiz_type").GetString());
        var ends = body.GetProperty("ends_at").GetString()!;
        Assert.EndsWith("Z", ends);
        Assert.Equal(endsAt.UtcDateTime, DateTimeOffset.Parse(ends, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Sem_evento_ativo_responde_200_com_has_event_false_e_padroes()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        await ResetAndSeedAsync(web, new LiveEvent("Antigo", "", QuizType.Cultural, DateTimeOffset.UtcNow.AddMinutes(-1)));
        using var client = web.CreateStudentClient(Guid.NewGuid());

        var response = await client.GetAsync("/api/live-event", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(body.GetProperty("has_event").GetBoolean());
        foreach (var field in new[] { "id", "title", "subtitle", "quiz_type" })
        {
            Assert.Equal("", body.GetProperty(field).GetString());
        }

        Assert.EndsWith("Z", body.GetProperty("ends_at").GetString());
    }

    [Fact]
    public async Task Com_mais_de_um_ativo_vale_o_que_termina_primeiro_e_expirado_nao_aparece()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var now = DateTimeOffset.UtcNow;
        var soon = new LiveEvent("Termina antes", "", QuizType.Esportivo, now.AddHours(1));
        await ResetAndSeedAsync(
            web, new LiveEvent("Termina depois", "", QuizType.Ambiental, now.AddHours(5)), soon,
            new LiveEvent("Expirado", "", QuizType.Cultural, now.AddHours(-1)));
        using var client = web.CreateStudentClient(Guid.NewGuid());

        var body = await client.GetFromJsonAsync<JsonElement>("/api/live-event", Ct);

        Assert.Equal(soon.Id.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("esportivo", body.GetProperty("quiz_type").GetString());
    }

    [Fact]
    public async Task Sem_token_responde_401()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/live-event", Ct)).StatusCode);
    }

    private static async Task<Guid> ResetAndSeedAsync(WebApplicationFactory<Program> web, params LiveEvent[] events)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        await db.LiveEvents.ExecuteDeleteAsync(Ct);
        db.LiveEvents.AddRange(events);
        await db.SaveChangesAsync(Ct);
        return events[0].Id;
    }
}
