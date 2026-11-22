using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel (Dashboard) no tema Recreio, fatia 3 (#165): mapa de calor, "Acabou de acontecer" e remoção da atividade recente antiga.</summary>
[Collection(PostgresCollection.Name)]
public partial class DashboardRecreioCalorFeedTests(PostgresFixture postgres, DashboardRecreioCalorFeedTests.SharedWorld shared)
    : IClassFixture<DashboardRecreioCalorFeedTests.SharedWorld>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Grupo 1: número visível; grupo 2: dia e turno em texto para leitor de tela (B2 da revisão do #187).
    [GeneratedRegex("""<td style="--v:[0-9.]+" data-tip="[^"]*" tabindex="0"><span><span class="sr">([^<]*): </span>([0-9.]+)<span class="sr"> respostas</span></span></td>""")]
    private static partial Regex HeatCell();

    [GeneratedRegex("""^(Segunda|Terça|Quarta|Quinta|Sexta|Sábado|Domingo), (manhã|tarde|noite)$""")]
    private static partial Regex DiaETurno();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();

    [GeneratedRegex(@"\.heat tbody td:hover > span,\s*\.heat tbody td:focus-visible > span\s*\{\s*opacity:\s*1;?\s*\}")]
    private static partial Regex VisivelNoFoco();

    [GeneratedRegex(@"@media\s*\(hover:\s*none\)\s*\{\s*\.heat tbody td > span\s*\{\s*opacity:\s*1;?\s*\}\s*\}")]
    private static partial Regex VisivelNoToque();

    [GeneratedRegex(@"(?<![:\w-])\.heat tbody td > span\s*\{([^}]*)\}")]
    private static partial Regex RotuloDoNumero();

    private static int Total(string html) =>
        HeatCell().Matches(html).Sum(m => int.Parse(m.Groups[2].Value, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.GetCultureInfo("pt-BR")));

    internal sealed record Seeded(HelinhoWebFactory Factory, Guid UnitA, Guid UnitB, Guid UnitOld);

    /// <summary>
    /// Um host para a classe inteira, com o mundo semeado uma vez; os testes só leem, então a ordem não importa.
    /// Cada host deixa observadores de arquivo (inotify no Linux), e o runner do CI limita a 128 instâncias (#215).
    /// </summary>
    public sealed class SharedWorld : IAsyncDisposable
    {
        private Seeded? _world;

        internal async Task<Seeded> GetAsync(PostgresFixture postgres)
        {
            if (_world is null)
            {
                var factory = new HelinhoWebFactory(postgres);
                try
                {
                    await factory.EnsureDatabaseAsync();
                    _world = await SeedAsync(factory);
                }
                catch
                {
                    await factory.DisposeAsync();
                    throw;
                }
            }

            return _world;
        }

        public async ValueTask DisposeAsync()
        {
            if (_world is not null)
            {
                await _world.Factory.DisposeAsync();
            }
        }
    }

    // A: "Ana Sobrenomeunico", 3 respostas há minutos. B: "<u>Caio</u> Sobrenomedois", 2 respostas há minutos.
    // Velha: "<i>Eva</i> Antiga", 2 respostas há 3 dias (mapa tem dado; feed de 24 h não).
    private static async Task<Seeded> SeedAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Unit a = new("Unidade Feed A"), b = new("Unidade Feed B"), old = new("Unidade Feed Antiga");
        db.Units.AddRange(a, b, old);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dados = new[] { (a, "Ana Sobrenomeunico", 3, TimeSpan.FromMinutes(5)), (b, "<u>Caio</u> Sobrenomedois", 2, TimeSpan.FromMinutes(5)), (old, "<i>Eva</i> Antiga", 2, TimeSpan.FromDays(3)) };
        foreach (var (unit, name, count, ago) in dados)
        {
            var user = new AppUser { UserName = "feed-" + Guid.NewGuid().ToString("N")[..8], UnitId = unit.Id };
            db.Users.Add(user);
            var student = new Student(user.Id, unit.Id, "FEED-" + name[..3], name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
                new DateOnly(2018, 3, 1), "Responsável Fictício", "5A", today);
            db.Students.Add(student);
            await db.SaveChangesAsync(Ct);
            var session = new QuizSession(student.Id, QuizType.Ambiental, count, DateTimeOffset.UtcNow - ago - TimeSpan.FromMinutes(10));
            db.QuizSessions.Add(session);
            var questions = Enumerable.Range(0, count).Select(_ => new Question(QuizType.Ambiental, AgeBand.Age6To7, "Pergunta sintética?", "Explicação sintética.",
                [("A", true), ("B", false), ("C", false), ("D", false)])).ToList();
            db.Questions.AddRange(questions);
            db.QuizAnswers.AddRange(questions.Select((q, i) => new QuizAnswer(session.Id, q.Id, "a", true, 0, DateTimeOffset.UtcNow - ago - TimeSpan.FromMinutes(8 - i))));
            session.Complete(DateTimeOffset.UtcNow - ago);
            await db.SaveChangesAsync(Ct);
        }

        return new Seeded(factory, a.Id, b.Id, old.Id);
    }

    private async Task<(string Raw, string Text)> GetAsync(string role, Func<Seeded, Guid?> unit)
    {
        var seeded = await shared.GetAsync(postgres);
        using var client = seeded.Factory.CreateClientAs(role, unit(seeded));
        var response = await client.GetAsync("/Dashboard", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        return (raw, WebUtility.HtmlDecode(raw));
    }

    [Fact]
    public async Task Mapa_de_calor_tem_21_celulas_com_valor_em_texto_e_o_pico_e_o_feed_so_tem_primeiro_nome_e_unidade()
    {
        var (_, html) = await GetAsync(RoleNames.Master, _ => null);

        Assert.Equal(21, HeatCell().Count(html));
        Assert.Contains("<table class=\"heat\">", html, StringComparison.Ordinal);
        Assert.Contains("<caption class=\"sr\">", html, StringComparison.Ordinal);
        Assert.Contains("Pico:", html, StringComparison.Ordinal);
        Assert.Contains("Acabou de acontecer", html, StringComparison.Ordinal);
        Assert.Contains("<b>Ana</b>", html, StringComparison.Ordinal);
        Assert.Contains("Unidade Feed A", html, StringComparison.Ordinal);
        foreach (var proibido in new[] { "Sobrenomeunico", "Sobrenomedois", "000.000.000-00", "00.000.000-0", "Rua Sintética", "Responsável Fictício", "FEED-Ana", "Atividade recente" })
        {
            Assert.DoesNotContain(proibido, html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cada_celula_do_mapa_traz_dia_e_turno_em_texto_para_leitor_de_tela()
    {
        var (_, html) = await GetAsync(RoleNames.Master, _ => null);

        var rotulos = HeatCell().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(21, rotulos.Count);
        Assert.All(rotulos, r => Assert.Matches(DiaETurno(), r));
        Assert.Equal(21, rotulos.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Numero_da_celula_aparece_no_foco_do_teclado_e_fica_visivel_e_legivel_em_tela_de_toque()
    {
        var seeded = await shared.GetAsync(postgres);
        using var client = seeded.Factory.CreateAnonymousClient();
        var css = CssComment().Replace(await client.GetStringAsync("/css/theme.css", Ct), string.Empty);

        // Visível no hover, no foco do teclado e sempre em toque.
        Assert.Matches(VisivelNoFoco(), css);
        Assert.Matches(VisivelNoToque(), css);
        // Legível em qualquer intensidade: rótulo com o par de texto do tema, sem cor própria do escuro por cima.
        var rotulo = RotuloDoNumero().Match(css);
        Assert.True(rotulo.Success, "falta a regra base do número da célula");
        Assert.Matches(@"background:\s*var\(--stage\)", rotulo.Groups[1].Value);
        Assert.Matches(@"(^|;)\s*color:\s*var\(--ink\)", rotulo.Groups[1].Value);
        Assert.DoesNotContain("[data-theme=\"dark\"] .heat", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrador_so_ve_o_feed_e_o_mapa_da_propria_unidade()
    {
        var (_, html) = await GetAsync(RoleNames.Admin, s => s.UnitA);

        Assert.Contains("<b>Ana</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Caio", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Unidade Feed B", html, StringComparison.Ordinal);
        Assert.Equal(3, Total(html));
    }

    [Fact]
    public async Task Sem_jogada_nas_ultimas_24_horas_o_feed_mostra_o_estado_vazio_e_o_primeiro_nome_e_codificado()
    {
        var (raw, html) = await GetAsync(RoleNames.Admin, s => s.UnitOld);

        Assert.Contains("id=\"feed-vazio\"", html, StringComparison.Ordinal);
        Assert.Contains("Nenhuma jogada nas últimas 24 horas", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<ul class=\"feed\"", html, StringComparison.Ordinal);
        Assert.Equal(2, Total(html));
        Assert.DoesNotContain("<i>Eva</i>", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nome_com_marcacao_no_feed_sai_codificado()
    {
        var (raw, _) = await GetAsync(RoleNames.Master, _ => null);

        Assert.DoesNotContain("<u>Caio</u>", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;u&gt;Caio&lt;/u&gt;", raw, StringComparison.Ordinal);
    }
}
