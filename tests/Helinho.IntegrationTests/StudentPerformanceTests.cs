using System.Net;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1859 // consulta pela porta IAppDbContext, como o caso de uso

namespace Helinho.IntegrationTests;

/// <summary>Desempenho do aluno no painel (#18): lista com XP e nível, filtro por faixa, detalhe, segregação por unidade e estado vazio (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public class StudentPerformanceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 10);

    private sealed class FakeUser(string role, Guid? unitId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => null;

        public Guid? UnitId => unitId;

        public bool IsInRole(string r) => r == role;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed record Seed(Guid UnitA, Guid UnitB, Guid Ana, Guid Bia, Guid Caio);

    private async Task<(HelinhoWebFactory Factory, Seed Seed)> NewWorldAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitA = new Unit($"Desemp A {Guid.NewGuid():N}");
        var unitB = new Unit($"Desemp B {Guid.NewGuid():N}");
        db.Units.AddRange(unitA, unitB);
        Student Make(string name, Unit unit, DateOnly birth, int xp)
        {
            var appUser = new AppUser { UserName = $"perf{name}{Guid.NewGuid():N}", UnitId = unit.Id };
            db.Users.Add(appUser);
            var s = new Student(appUser.Id, unit.Id, $"PRF-{name}-{Guid.NewGuid().ToString("N")[..16]}", name, "11.111.111-1", "111.111.111-11",
                "Rua Sintética, 1", birth, "Responsável Fictício", "5A", Today);
            s.AddXp(xp);
            db.Students.Add(s);
            return s;
        }

        var ana = Make("Ana", unitA, new DateOnly(2019, 3, 1), 350); // nível 3 · Explorador
        var bia = Make("Bia", unitA, new DateOnly(2014, 3, 1), 0);   // sem respostas
        var caio = Make("Caio", unitB, new DateOnly(2019, 3, 1), 10);
        var session = new QuizSession(ana.Id, QuizType.Ambiental, 2, Now);
        db.QuizSessions.Add(session);
        var questionIds = await db.Questions.Select(x => x.Id).Take(2).ToListAsync(ct);
        db.QuizAnswers.AddRange(
            new QuizAnswer(session.Id, questionIds[0], "a", true, 10, Now),
            new QuizAnswer(session.Id, questionIds[1], "b", false, 0, Now));
        var badge = new Badge($"desemp-{Guid.NewGuid():N}", "Explorador do Bosque", "");
        db.Badges.Add(badge);
        db.StudentBadges.Add(new StudentBadge(ana.Id, badge.Id, Now));
        await db.SaveChangesAsync(ct);
        return (factory, new Seed(unitA.Id, unitB.Id, ana.Id, bia.Id, caio.Id));
    }

    private static async Task<T> WithDbAsync<T>(HelinhoWebFactory factory, string role, Guid? unitId, Func<IAppDbContext, Task<T>> body)
    {
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options, new FakeUser(role, unitId));
        return await body(context);
    }

    [Fact]
    public async Task Lista_mostra_xp_e_nivel_e_filtra_por_faixa()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;

        var todos = await WithDbAsync(factory, RoleNames.Admin, seed.UnitA,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), null, 1, ct));
        var faixa = await WithDbAsync(factory, RoleNames.Admin, seed.UnitA,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), AgeBand.Age11To12, 1, ct));

        var ana = todos.Rows.Single(r => r.Id == seed.Ana);
        Assert.Equal((350, 3, "Explorador"), (ana.Xp, ana.Level, ana.LevelName));
        Assert.Equal([seed.Bia], faixa.Rows.Select(r => r.Id));
    }

    [Fact]
    public async Task Admin_so_ve_alunos_da_propria_unidade_na_lista_e_no_detalhe_por_url_direta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;

        var lista = await WithDbAsync(factory, RoleNames.Admin, seed.UnitA,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), null, 1, ct));
        Assert.DoesNotContain(lista.Rows, r => r.Id == seed.Caio);

        using var client = factory.CreateClientAs(RoleNames.Admin, seed.UnitA);
        var own = await client.GetAsync($"/Desempenho/Aluno/{seed.Ana}", ct);
        var other = await client.GetAsync($"/Desempenho/Aluno/{seed.Caio}", ct);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        using var master = factory.CreateClientAs(RoleNames.Master);
        Assert.Equal(HttpStatusCode.OK, (await master.GetAsync($"/Desempenho/Aluno/{seed.Caio}", ct)).StatusCode);
    }

    [Fact]
    public async Task Detalhe_mostra_nivel_acertos_por_tipo_e_conquistas()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;

        var detail = await WithDbAsync(factory, RoleNames.Master, null,
            db => StudentPerformanceQueries.GetAsync(db, new FixedClock(), seed.Ana, ct));

        Assert.NotNull(detail);
        Assert.Equal(("Explorador", 600), (detail.LevelName, detail.NextLevelXp));
        var ambiental = detail.Scores.Single(s => s.Type == QuizType.Ambiental);
        Assert.Equal((2, 1), (ambiental.Answered, ambiental.Correct));
        Assert.Equal(0, detail.Scores.Single(s => s.Type == QuizType.Cultural).Answered);
        Assert.Equal(["Explorador do Bosque"], detail.Badges.Select(b => b.Title));

        using var client = factory.CreateClientAs(RoleNames.Master);
        var html = await (await client.GetAsync($"/Desempenho/Aluno/{seed.Ana}", ct)).Content.ReadAsStringAsync(ct);
        Assert.Contains("Nível 3 — Explorador", html, StringComparison.Ordinal);
        Assert.Contains("Explorador do Bosque", html, StringComparison.Ordinal);
        Assert.DoesNotContain("111.111.111-11", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aluno_sem_respostas_mostra_estado_vazio_e_nao_erro()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;

        using var client = factory.CreateClientAs(RoleNames.Admin, seed.UnitA);
        var response = await client.GetAsync($"/Desempenho/Aluno/{seed.Bia}", ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ainda não respondeu nenhuma pergunta", html, StringComparison.Ordinal);
        Assert.Contains("Nenhuma conquista desbloqueada", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ficha_usa_marcacao_recreio_com_personagem_selo_e_sem_pii()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var art = new Badge($"cem-{Guid.NewGuid():N}", "Cem perguntas", "");
            db.Badges.Add(art);
            db.StudentBadges.Add(new StudentBadge(seed.Ana, art.Id, Now));
            await db.SaveChangesAsync(ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var html = await (await client.GetAsync($"/Desempenho/Aluno/{seed.Ana}", ct)).Content.ReadAsStringAsync(ct);

        Assert.Contains("class=\"profile panel panel-pad\"", html, StringComparison.Ordinal);
        Assert.Contains("Ambiental · Flora", html, StringComparison.Ordinal);
        Assert.Contains("Diversidade · Lia", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/img/selo-cem-perguntas.webp\" alt=\"Selo Cem perguntas\" width=\"96\" height=\"109\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Selo Explorador do Bosque\"", html, StringComparison.Ordinal); // selo sem arte: ícone com nome
        Assert.Contains("Desbloqueado em", html, StringComparison.Ordinal);
        Assert.DoesNotContain("btn-ai", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Rua Sintética", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lista_usa_marcacao_recreio_com_aplicar_e_resumo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await NewWorldAsync();
        await using var _ = factory;

        using var client = factory.CreateClientAs(RoleNames.Admin, seed.UnitA);
        var html = await (await client.GetAsync("/Desempenho", ct)).Content.ReadAsStringAsync(ct);
        var vazio = await (await client.GetAsync("/Desempenho?faixa=Age13To14", ct)).Content.ReadAsStringAsync(ct);

        Assert.Contains("class=\"filters-bar\"", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"tag tag-neutral\"", html, StringComparison.Ordinal);
        Assert.Contains("2 alunos", html, StringComparison.Ordinal);
        Assert.DoesNotContain("111.111.111-11", html, StringComparison.Ordinal);
        Assert.Contains("id=\"desempenho-vazio\"", vazio, StringComparison.Ordinal);
        Assert.Contains("width=\"117\" height=\"120\"", vazio, StringComparison.Ordinal);
    }
}
