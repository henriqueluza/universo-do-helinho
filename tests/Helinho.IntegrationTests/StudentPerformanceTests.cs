using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1859 // consulta pela porta IAppDbContext, como o caso de uso

namespace Helinho.IntegrationTests;

/// <summary>Desempenho do aluno no painel (#18): lista com XP e nível, filtro por faixa, detalhe, segregação por unidade e estado vazio (dados sintéticos).</summary>
[Collection(PostgresCollection.Name)]
public partial class StudentPerformanceTests(PostgresFixture postgres, StudentPerformanceTests.SharedWorld shared)
    : IClassFixture<StudentPerformanceTests.SharedWorld>
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

    internal sealed record Seed(Guid UnitA, Guid UnitB, Guid Ana, Guid Bia, Guid Caio);

    /// <summary>
    /// Um host para a classe, com o mundo semeado uma vez e o selo "Cem perguntas" (com arte) da Ana. Só testes que
    /// apenas leem esse mundo o usam. Cada host deixa observadores de arquivo (inotify no Linux), e o runner do CI
    /// limita a 128 instâncias (#215).
    /// </summary>
    public sealed class SharedWorld : IAsyncDisposable
    {
        private (HelinhoWebFactory Factory, Seed Seed)? _world;

        internal async Task<(HelinhoWebFactory Factory, Seed Seed)> GetAsync(PostgresFixture postgres)
        {
            if (_world is null)
            {
                var world = await NewWorldAsync(postgres);
                await GiveBadgeAsync(world.Factory, world.Seed.Ana, "Cem perguntas");
                _world = world;
            }

            return _world.Value;
        }

        public async ValueTask DisposeAsync()
        {
            if (_world is { } world)
            {
                await world.Factory.DisposeAsync();
            }
        }
    }

    private static async Task<(HelinhoWebFactory Factory, Seed Seed)> NewWorldAsync(PostgresFixture postgres)
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
        var sessionCaio = new QuizSession(caio.Id, QuizType.Ambiental, 1, Now);
        db.QuizSessions.Add(sessionCaio);
        db.QuizAnswers.Add(new QuizAnswer(sessionCaio.Id, questionIds[0], "a", true, 10, Now));
        var badge = new Badge($"desemp-{Guid.NewGuid():N}", "Explorador do Bosque", "");
        db.Badges.Add(badge);
        db.StudentBadges.Add(new StudentBadge(ana.Id, badge.Id, Now));
        await db.SaveChangesAsync(ct);
        return (factory, new Seed(unitA.Id, unitB.Id, ana.Id, bia.Id, caio.Id));
    }

    private static async Task<T> WithDbAsync<T>(
        HelinhoWebFactory factory, string role, Guid? unitId, Func<IAppDbContext, Task<T>> body, Action<string>? onCommand = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString);
        if (onCommand is not null)
        {
            options.LogTo(onCommand, [RelationalEventId.CommandExecuted]);
        }

        await using var context = new AppDbContext(options.Options, new FakeUser(role, unitId));
        return await body(context);
    }

    [Fact]
    public async Task Lista_mostra_xp_e_nivel_e_filtra_por_faixa()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await shared.GetAsync(postgres);

        var todos = await WithDbAsync(factory, RoleNames.Admin, seed.UnitA,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), null, 1, ct));
        var faixa = await WithDbAsync(factory, RoleNames.Admin, seed.UnitA,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), AgeBand.Age11To12, 1, ct));

        var ana = todos.Rows.Single(r => r.Id == seed.Ana);
        Assert.Equal((350, 3, "Explorador"), (ana.Xp, ana.Level, ana.LevelName));
        Assert.Equal([seed.Bia], faixa.Rows.Select(r => r.Id));
        Assert.Equal((2, 1), (ana.Answered, ana.Correct));
        var bia = todos.Rows.Single(r => r.Id == seed.Bia);
        Assert.Equal((0, 0), (bia.Answered, bia.Correct));
    }

    [Fact]
    public async Task Respostas_e_acertos_ficam_na_unidade_do_admin_e_sem_consulta_por_aluno()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await shared.GetAsync(postgres);
        var comandos = 0;

        var listaB = await WithDbAsync(factory, RoleNames.Admin, seed.UnitB,
            db => StudentPerformanceQueries.ListAsync(db, new FixedClock(), null, 1, ct), _ => comandos++);

        var caio = Assert.Single(listaB.Rows);
        Assert.Equal((seed.Caio, 1, 1), (caio.Id, caio.Answered, caio.Correct));
        Assert.Equal(2, comandos); // contagem + página; respostas e acertos vêm na mesma consulta (sem N+1)
    }

    [Fact]
    public async Task Admin_so_ve_alunos_da_propria_unidade_na_lista_e_no_detalhe_por_url_direta()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await shared.GetAsync(postgres);

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
        var (factory, seed) = await NewWorldAsync(postgres); // mundo próprio: confere a lista exata de selos
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
        var (factory, seed) = await shared.GetAsync(postgres);

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
        var (factory, seed) = await shared.GetAsync(postgres); // a Ana tem o selo "Cem perguntas", com arte entregue

        using var client = factory.CreateClientAs(RoleNames.Master);
        var html = await (await client.GetAsync($"/Desempenho/Aluno/{seed.Ana}", ct)).Content.ReadAsStringAsync(ct);

        Assert.True(HasClasses(html, "profile", "panel"), "ficha sem o bloco .profile");
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
        var (factory, seed) = await shared.GetAsync(postgres);

        using var client = factory.CreateClientAs(RoleNames.Admin, seed.UnitA);
        var html = await (await client.GetAsync("/Desempenho", ct)).Content.ReadAsStringAsync(ct);
        var vazio = await (await client.GetAsync("/Desempenho?faixa=Age13To14", ct)).Content.ReadAsStringAsync(ct);

        Assert.True(HasClasses(html, "page-head"), "lista sem o cabeçalho da página");
        Assert.True(HasClasses(html, "filters-bar"), "lista sem a barra de filtros");
        Assert.True(HasClasses(html, "table"), "lista sem a tabela do tema");
        Assert.Contains("<th scope=\"row\"><a class=\"name\"", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.True(HasClasses(html, "tag", "tag-neutral"), "nível sem a tag do tema");
        Assert.Contains("2 alunos", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"col\">Acerto</th>", html, StringComparison.Ordinal);
        var linhaAna = LinhaDoTipo(">Ana</a>").Match(html);
        Assert.True(linhaAna.Success, "lista sem a linha da Ana");
        Assert.Matches(BarraDecorativa(50), linhaAna.Value);
        Assert.Contains("<span>50%</span>", linhaAna.Value, StringComparison.Ordinal);
        var linhaBia = LinhaDoTipo(">Bia</a>").Match(html);
        Assert.True(linhaBia.Success, "lista sem a linha da Bia");
        Assert.Contains("<span>—</span>", linhaBia.Value, StringComparison.Ordinal); // sem respostas: sem barra e sem 0% inventado
        Assert.DoesNotContain("class=\"meter", linhaBia.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(">0%<", linhaBia.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("111.111.111-11", html, StringComparison.Ordinal);
        Assert.Contains("id=\"desempenho-vazio\"", vazio, StringComparison.Ordinal);
        Assert.Contains("width=\"117\" height=\"120\"", vazio, StringComparison.Ordinal);
        AssertSemScriptInlineNemOnchange(html);
        AssertSemScriptInlineNemOnchange(vazio);
    }

    [Fact]
    public async Task Ficha_mostra_nivel_aproveitamento_e_selos_em_texto_alem_das_barras()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, seed) = await shared.GetAsync(postgres); // a Ana tem o selo "Cem perguntas", com arte entregue

        using var client = factory.CreateClientAs(RoleNames.Master);
        var html = await (await client.GetAsync($"/Desempenho/Aluno/{seed.Ana}", ct)).Content.ReadAsStringAsync(ct);

        // .level: 350 de 600 XP -> barra decorativa em 58% e o valor sempre em texto.
        var nivel = BlocoNivel().Match(html);
        Assert.True(nivel.Success, "ficha sem o bloco .level#aluno-nivel");
        Assert.Contains("Nível 3 — Explorador", nivel.Value, StringComparison.Ordinal);
        Assert.Contains("350 XP", nivel.Value, StringComparison.Ordinal);
        Assert.Contains("Próximo nível em 600 XP", nivel.Value, StringComparison.Ordinal);
        Assert.Matches(BarraDecorativa(58), nivel.Value);

        // Ambiental: 2 respostas, 1 acerto -> 50% na barra (aria-hidden) e "50%" em texto na mesma célula.
        var ambiental = LinhaDoTipo("Ambiental · Flora").Match(html);
        Assert.True(ambiental.Success, "tabela sem a linha do tipo Ambiental");
        Assert.Matches(BarraDecorativa(50), ambiental.Value);
        Assert.Contains("<span>50%</span>", ambiental.Value, StringComparison.Ordinal);
        var cultural = LinhaDoTipo("Cultural · Luna").Match(html);
        Assert.True(cultural.Success, "tabela sem a linha do tipo Cultural");
        Assert.Contains("<span>—</span>", cultural.Value, StringComparison.Ordinal); // sem respostas: sem barra e sem 0% inventado
        Assert.DoesNotContain("class=\"meter", cultural.Value, StringComparison.Ordinal);

        // .badges > .badge-card: cada selo é um cartão com o nome em texto, o nome acessível e a data no fuso de Brasília.
        var cartoes = CartaoDeSelo().Matches(html).Select(m => m.Groups["c"].Value).ToList();
        Assert.Equal(2, cartoes.Count);
        var cem = Assert.Single(cartoes, c => c.Contains("Cem perguntas", StringComparison.Ordinal));
        Assert.Contains("alt=\"Selo Cem perguntas\"", cem, StringComparison.Ordinal);
        Assert.Contains("Desbloqueado em 10/11/2026", cem, StringComparison.Ordinal);
        var semArte = Assert.Single(cartoes, c => c.Contains("Explorador do Bosque", StringComparison.Ordinal));
        Assert.Contains("role=\"img\" aria-label=\"Selo Explorador do Bosque\"", semArte, StringComparison.Ordinal);
        AssertSemScriptInlineNemOnchange(html);
    }

    /// <summary>Contrato da #153: filtros com botão Aplicar, sem onchange, &lt;noscript&gt; nem script inline.</summary>
    private static void AssertSemScriptInlineNemOnchange(string html)
    {
        Assert.DoesNotContain("<noscript", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onchange=", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("""<script(?![^>]*\bsrc=)""", html);
    }

    private static async Task GiveBadgeAsync(HelinhoWebFactory factory, Guid studentId, string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var badge = new Badge($"selo-{Guid.NewGuid():N}", title, "");
        db.Badges.Add(badge);
        db.StudentBadges.Add(new StudentBadge(studentId, badge.Id, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Há um elemento cujo atributo class tem todas as classes pedidas, em qualquer ordem.</summary>
    private static bool HasClasses(string html, params string[] classes) =>
        AtributoClass().Matches(html).Any(m =>
        {
            var tokens = m.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return classes.All(tokens.Contains);
        });

    private static Regex LinhaDoTipo(string nome) =>
        new($"<tr>(?:(?!</tr>)[\\s\\S])*{Regex.Escape(nome)}(?:(?!</tr>)[\\s\\S])*</tr>", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Regex BarraDecorativa(int percent) =>
        new($"<span class=\"[^\"]*\\bmeter\\b[^\"]*\" aria-hidden=\"true\"[^>]*><i style=\"width: {percent}%\"></i></span>", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex("class=\"(?<c>[^\"]*)\"", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AtributoClass();

    // .level e .badge-card não têm <div> aninhado: o bloco vai até o primeiro </div>. Se ganharem, troque o fim do padrão.
    [GeneratedRegex("""<div class="[^"]*\blevel\b[^"]*" id="aluno-nivel">[\s\S]*?</div>""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BlocoNivel();

    [GeneratedRegex("""<div class="[^"]*\bbadge-card\b[^"]*">(?<c>[\s\S]*?)</div>""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CartaoDeSelo();
}
