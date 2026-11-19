using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel (Dashboard) no tema Recreio, fatia 2 (#165): acertos por tipo, atenção e rankings. Dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public partial class DashboardRecreioTiposTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string EnunciadoA = "<script>alert('xss')</script> Qual é o maior bioma?";
    private const string EnunciadoB = "Pergunta exclusiva da unidade B";

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex ImgTag();

    // Duas unidades, cada uma com 1 aluno que errou 6 vezes uma questão própria (acerto 0%, acima do mínimo de 5 respostas).
    private static async Task<Guid> SeedAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Unit a = new("Unidade Tipos A"), b = new("Unidade Tipos B");
        db.Units.AddRange(a, b);
        var users = new[] { new AppUser { UserName = "tipos-a", UnitId = a.Id }, new AppUser { UserName = "tipos-b", UnitId = b.Id } };
        db.Users.AddRange(users);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Student Novo(AppUser u, Unit unit, string name) => new(u.Id, unit.Id, $"TIP-{name}", name, "00.000.000-0", "000.000.000-00", "Rua Sintética, 1",
            new DateOnly(2018, 3, 1), "Responsável Fictício", "5A", today);
        Student sa = Novo(users[0], a, "Ana"), sb = Novo(users[1], b, "Caio");
        db.Students.AddRange(sa, sb);
        await db.SaveChangesAsync(Ct);
        foreach (var (student, statement) in new[] { (sa, EnunciadoA), (sb, EnunciadoB) })
        {
            var question = new Question(QuizType.Cultural, AgeBand.Age6To7, statement, "Explicação sintética.", [("A", true), ("B", false), ("C", false), ("D", false)]);
            db.Questions.Add(question);
            // Uma resposta por (sessão, questão): seis sessões, cada uma com um erro na mesma questão.
            for (var i = 0; i < 6; i++)
            {
                var session = new QuizSession(student.Id, QuizType.Cultural, 10, DateTimeOffset.UtcNow.AddMinutes(-30 - i));
                db.QuizSessions.Add(session);
                db.QuizAnswers.Add(new QuizAnswer(session.Id, question.Id, "b", false, 0, DateTimeOffset.UtcNow.AddMinutes(-10 - i)));
            }
        }

        await db.SaveChangesAsync(Ct);
        return a.Id;
    }

    private async Task<string> GetAsync(string role, bool asUnitA) => WebUtility.HtmlDecode(await GetRawAsync(role, asUnitA));

    private async Task<string> GetRawAsync(string role, bool asUnitA)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        var unitA = await SeedAsync(factory);
        using var client = factory.CreateClientAs(role, asUnitA ? unitA : null);
        var response = await client.GetAsync("/Dashboard", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Acertos_por_tipo_mostram_os_cinco_temas_com_nome_e_personagem_e_imagens_dimensionadas()
    {
        var html = await GetAsync(RoleNames.Master, false);

        Assert.Equal(5, Regex.Count(html, "class=\"castcard "));
        foreach (var texto in new[] { "Ambiental", "Conhecimento", "Esportivo", "Cultural", "Diversidade", "com a Flora", "com o Byte", "com o Max", "com a Luna", "com a Lia" })
        {
            Assert.Contains(texto, html, StringComparison.Ordinal);
        }

        foreach (var img in ImgTag().Matches(html).Cast<Match>())
        {
            Assert.Contains("width=\"", img.Value, StringComparison.Ordinal);
            Assert.Contains("height=\"", img.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Master_ve_as_duas_unidades_no_ranking_e_o_enunciado_e_codificado()
    {
        var html = await GetAsync(RoleNames.Master, false);

        Assert.Contains("Unidades que mais jogam", html, StringComparison.Ordinal);
        Assert.Contains("Unidade Tipos A", html, StringComparison.Ordinal);
        Assert.Contains("Unidade Tipos B", html, StringComparison.Ordinal);
        Assert.Contains(EnunciadoB, html, StringComparison.Ordinal);
        var raw = await GetRawAsync(RoleNames.Master, false);
        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrador_nao_recebe_o_bloco_de_unidades_nem_dados_da_outra_unidade()
    {
        var html = await GetAsync(RoleNames.Admin, true);

        Assert.DoesNotContain("Unidades que mais jogam", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Unidade Tipos B", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EnunciadoB, html, StringComparison.Ordinal);
        Assert.Contains("Questões que mais confundem", html, StringComparison.Ordinal);
        Assert.Contains("Qual é o maior bioma?", html, StringComparison.Ordinal);
        var raw = await GetRawAsync(RoleNames.Admin, true);
        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alerta_de_acerto_baixo_leva_a_rota_que_existe_e_o_botao_revisar_abre_a_questao()
    {
        var html = await GetAsync(RoleNames.Admin, true);

        Assert.Contains("Precisa de atenção", html, StringComparison.Ordinal);
        Assert.Matches("""<a class="attn" href="/Desempenho"><span class="ic ic-q cul".*Cultural abaixo de 65%""", html);
        Assert.Matches("""href="/Questoes/Editar/[0-9a-f-]{36}">Revisar""", html);
        Assert.DoesNotContain("href=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unidade_sem_respostas_nao_desenha_cartoes_de_tipo()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        Guid empty;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = new Unit("Unidade Tipos Vazia");
            db.Units.Add(unit);
            await db.SaveChangesAsync(Ct);
            empty = unit.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, empty);
        var html = await client.GetStringAsync("/Dashboard", Ct);

        Assert.Contains("Nenhuma resposta neste período.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"castcard", html, StringComparison.Ordinal);
    }
}
