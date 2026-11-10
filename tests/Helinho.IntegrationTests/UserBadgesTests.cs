using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Helinho.Application.Quizzes;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.UnitTests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>GET /api/user/badges: paginação fixa no servidor (P11) e `unlocked` do aluno autenticado.</summary>
[Collection(PostgresCollection.Name)]
public class UserBadgesTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lista_paginada_reflete_as_conquistas_do_aluno_autenticado()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var prefix = $"t{Guid.NewGuid():N}"[..9];
        var extra = 5;
        var (ana, bia) = await SeedAsync(web, prefix, GetUserBadges.PageSize + extra);
        using var client = web.CreateStudentClient(ana);

        var response = await client.GetAsync("/api/user/badges", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Empty(JsonUtilityContract.Verify(json));
        var body = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        var badges = body.GetProperty("badges").EnumerateArray().ToList();
        Assert.Equal(GetUserBadges.PageSize, badges.Count);
        Assert.All(badges, b =>
        {
            Assert.True(b.TryGetProperty("id", out _));
            Assert.True(b.TryGetProperty("title", out _));
            Assert.True(b.TryGetProperty("visual", out _));
        });

        var mine = await AllMyBadgesAsync(client, prefix);
        Assert.Equal([$"{prefix}-00", $"{prefix}-01"], mine.Where(b => b.Value).Select(b => b.Key).Order());
        using var otherClient = web.CreateStudentClient(bia);
        var others = await AllMyBadgesAsync(otherClient, prefix);
        Assert.Equal([$"{prefix}-01"], others.Where(b => b.Value).Select(b => b.Key));
    }

    [Fact]
    public async Task Pagina_invalida_responde_400_e_alem_do_fim_devolve_lista_vazia()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        var web = factory.WithTestSessions();
        var (ana, _) = await SeedAsync(web, $"t{Guid.NewGuid():N}"[..9], 3);
        using var client = web.CreateStudentClient(ana);

        foreach (var invalid in new[] { "0", "-1", "abc", "99999999999999999999" })
        {
            var bad = await client.GetAsync($"/api/user/badges?page={invalid}", Ct);
            Assert.True(HttpStatusCode.BadRequest == bad.StatusCode, $"page={invalid}");
            var error = await bad.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.True("invalid_page" == error.GetProperty("error").GetString(), $"page={invalid}");
        }

        var huge = await client.GetFromJsonAsync<JsonElement>($"/api/user/badges?page={int.MaxValue}", Ct);
        Assert.Empty(huge.GetProperty("badges").EnumerateArray());

        var first = await client.GetFromJsonAsync<JsonElement>("/api/user/badges?page=1", Ct);
        var totalPages = first.GetProperty("total_pages").GetInt32();
        var beyond = await client.GetFromJsonAsync<JsonElement>($"/api/user/badges?page={totalPages + 1}", Ct);
        Assert.Empty(beyond.GetProperty("badges").EnumerateArray());
        Assert.Equal(totalPages + 1, beyond.GetProperty("page").GetInt32());
        Assert.Equal(totalPages, beyond.GetProperty("total_pages").GetInt32());
    }

    [Fact]
    public async Task Sem_token_responde_401_e_aluno_inexistente_403()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using var anonymous = factory.CreateClient();
        using var unknown = factory.WithTestSessions().CreateStudentClient(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/user/badges", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unknown.GetAsync("/api/user/badges", Ct)).StatusCode);
    }

    /// <summary>Percorre todas as páginas e devolve só as conquistas do teste (prefixo), com o estado `unlocked`.</summary>
    private static async Task<Dictionary<string, bool>> AllMyBadgesAsync(HttpClient client, string prefix)
    {
        var result = new Dictionary<string, bool>();
        var first = await client.GetFromJsonAsync<JsonElement>("/api/user/badges?page=1", Ct);
        for (var page = 1; page <= first.GetProperty("total_pages").GetInt32(); page++)
        {
            var body = await client.GetFromJsonAsync<JsonElement>($"/api/user/badges?page={page}", Ct);
            foreach (var b in body.GetProperty("badges").EnumerateArray())
            {
                var id = b.GetProperty("id").GetString()!;
                if (id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result[id] = b.GetProperty("unlocked").GetBoolean();
                }
            }
        }

        return result;
    }

    private static async Task<(Guid Ana, Guid Bia)> SeedAsync(WebApplicationFactory<Program> web, string prefix, int badgeCount)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(Ct);
        var unit = new Unit($"Unidade {prefix}");
        db.Units.Add(unit);
        var ana = NewStudent(db, unit, $"{prefix}a");
        var bia = NewStudent(db, unit, $"{prefix}b");
        var badges = Enumerable.Range(0, badgeCount)
            .Select(i => new Badge($"{prefix}-{i:00}", $"Conquista {i}", $"visual-{i}")).ToList();
        db.Badges.AddRange(badges);
        var now = DateTimeOffset.UtcNow;
        db.StudentBadges.AddRange(
            new StudentBadge(ana, badges[0].Id, now), new StudentBadge(ana, badges[1].Id, now),
            new StudentBadge(bia, badges[1].Id, now));
        await db.SaveChangesAsync(Ct);
        return (ana, bia);
    }

    private static Guid NewStudent(AppDbContext db, Unit unit, string suffix)
    {
        var user = new AppUser { UserName = $"aluno{suffix}", UnitId = unit.Id };
        db.Users.Add(user);
        var student = new Student(
            user.Id, unit.Id, $"P-{suffix}", "Aluno Fictício", "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2016, 1, 1), "Responsável Fictício", "5A", new DateOnly(2026, 11, 10));
        db.Students.Add(student);
        return student.Id;
    }
}
