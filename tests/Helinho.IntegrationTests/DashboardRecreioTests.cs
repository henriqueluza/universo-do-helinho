using System.Text.RegularExpressions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Pages.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel (Dashboard) no tema Recreio, fatia 1 (#165): cena, filtros, indicadores e respostas por dia. Dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public partial class DashboardRecreioTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("""id="kpi-respostas"><b data-count="(\d+)">""")]
    private static partial Regex RespostasCount();

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex ImgTag();

    private sealed record Seeded(Guid UnitA, Guid UnitB, Guid Empty);

    // Unidade A: 1 aluno, 2 respostas hoje. Unidade B: 1 aluno, 3 respostas hoje. Unidade vazia: sem aluno.
    private static async Task<Seeded> SeedAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Unit a = new("Unidade Recreio A"), b = new("Unidade Recreio B"), empty = new("Unidade Recreio Vazia");
        db.Units.AddRange(a, b, empty);
        var users = new[] { new AppUser { UserName = "recreio-a", UnitId = a.Id }, new AppUser { UserName = "recreio-b", UnitId = b.Id } };
        db.Users.AddRange(users);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Student Novo(AppUser u, Unit unit, string name) => new(u.Id, unit.Id, $"REC-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2018, 3, 1), "Responsável Fictício", "5A", today);
        Student sa = Novo(users[0], a, "Ana"), sb = Novo(users[1], b, "Caio");
        db.Students.AddRange(sa, sb);
        await db.SaveChangesAsync(Ct);
        foreach (var (student, count) in new[] { (sa, 2), (sb, 3) })
        {
            var session = new QuizSession(student.Id, QuizType.Ambiental, 10, DateTimeOffset.UtcNow.AddMinutes(-30));
            db.QuizSessions.Add(session);
            var questions = Enumerable.Range(0, count).Select(_ => new Question(QuizType.Ambiental, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
                [("A", true), ("B", false), ("C", false), ("D", false)])).ToList();
            db.Questions.AddRange(questions);
            db.QuizAnswers.AddRange(questions.Select(q => new QuizAnswer(session.Id, q.Id, "a", true, 0, DateTimeOffset.UtcNow.AddMinutes(-10))));
        }

        await db.SaveChangesAsync(Ct);
        return new Seeded(a.Id, b.Id, empty.Id);
    }

    private async Task<string> GetAsync(string role, Func<Seeded, Guid?> unit, string query)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        var seeded = await SeedAsync(factory);
        using var client = factory.CreateClientAs(role, unit(seeded));
        return System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Dashboard" + query, Ct));
    }

    [Fact]
    public async Task Master_ve_cena_filtros_indicadores_e_tabela_dos_graficos_com_as_classes_do_Recreio()
    {
        var html = await GetAsync(RoleNames.Master, _ => null, "?dias=7");

        foreach (var marcador in new[] { "class=\"scene\"", "class=\"filters\"", "class=\"kpis\"", "class=\"kpi\"", "class=\"scene-stats\"", "class=\"week\"",
                     "class=\"linechart\"", "class=\"chart-table\"", "<table id=\"tab-resp\"", "data-count=\"" })
        {
            Assert.Contains(marcador, html, StringComparison.Ordinal);
        }

        Assert.Contains("Alunos ativos", html, StringComparison.Ordinal);
        Assert.Contains("Taxa de acerto", html, StringComparison.Ordinal);
        Assert.Contains("Sem jogar há 7+ dias", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Count(html, """<th scope="row" data-curto="""));
        Assert.DoesNotContain("onchange", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<noscript", html, StringComparison.OrdinalIgnoreCase);
        // Lilás e IA fora.
        Assert.DoesNotContain("btn-ai", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tag-ai", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrador_so_ve_os_numeros_da_propria_unidade_e_master_ve_a_soma()
    {
        var admin = await GetAsync(RoleNames.Admin, s => s.UnitA, "");
        var master = await GetAsync(RoleNames.Master, _ => null, "");

        var doAdmin = int.Parse(RespostasCount().Match(admin).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var doMaster = int.Parse(RespostasCount().Match(master).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(2, doAdmin);
        // Sem período anterior com respostas: texto da variação, sem seta e sem cor de bom/ruim.
        Assert.Contains("sem dados no período anterior", admin, StringComparison.Ordinal);
        Assert.Contains("class=\"delta flat\"", admin, StringComparison.Ordinal);
        Assert.True(doMaster >= 5, $"Master deveria somar as duas unidades, achou {doMaster}");
        Assert.DoesNotContain("id=\"unidade\"", admin, StringComparison.Ordinal);
        Assert.Contains("id=\"unidade\"", master, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unidade_sem_dados_mostra_a_mensagem_de_vazio_sem_indicadores_e_com_imagens_dimensionadas()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        var seeded = await SeedAsync(factory);
        using var client = factory.CreateClientAs(RoleNames.Master);
        var vazio = await client.GetStringAsync($"/Dashboard?unidade={seeded.Empty}", Ct);
        var html = await client.GetStringAsync("/Dashboard", Ct);

        Assert.Contains("Nenhuma resposta neste período.", vazio, StringComparison.Ordinal);
        Assert.Contains("id=\"dashboard-vazio\"", vazio, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"kpis\"", vazio, StringComparison.Ordinal);
        Assert.DoesNotContain("tab-resp", vazio, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", vazio, StringComparison.Ordinal);
        foreach (var img in ImgTag().Matches(html).Concat(ImgTag().Matches(vazio)))
        {
            Assert.Contains("width=\"", img.Value, StringComparison.Ordinal);
            Assert.Contains("height=\"", img.Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(TrendDirection.Up, true, "good")]
    [InlineData(TrendDirection.Up, false, "bad")]
    [InlineData(TrendDirection.Down, true, "bad")]
    [InlineData(TrendDirection.Down, false, "good")]
    [InlineData(TrendDirection.Up, null, "flat")]
    [InlineData(TrendDirection.None, true, "flat")]
    [InlineData(TrendDirection.Flat, false, "flat")]
    public void Cor_da_variacao_depende_da_direcao_e_do_indicador(TrendDirection direction, bool? upIsGood, string esperado)
    {
        Assert.Equal(esperado, IndexModel.DeltaClass(new Trend(direction, "x"), upIsGood));
    }

    [Fact]
    public void Formatadores_usam_ponto_de_milhar_serie_sem_buracos_e_nome_do_dia()
    {
        Assert.Equal("13.249", IndexModel.Num(13249));
        Assert.Equal("1,5".Replace(',', '.'), IndexModel.Inv(1.5));
        Assert.Equal("70,72", IndexModel.Spark([70, null, 72]));
        Assert.Null(IndexModel.Spark([70, null]));
        Assert.Equal("qui, 15/out", IndexModel.DayLabel(new DateOnly(2026, 10, 15)));
        Assert.Equal("Quinta", IndexModel.DayName(new DateOnly(2026, 10, 15)));
    }
}
