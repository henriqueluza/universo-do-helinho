using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Pages.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Painel (Dashboard) no tema Recreio, fatia 2 (#165): acertos por tipo, atenção e rankings. Dados sintéticos.</summary>
[Collection(PostgresCollection.Name)]
public partial class DashboardRecreioTiposTests(PostgresFixture postgres, DashboardRecreioTiposTests.SharedWorld shared)
    : IClassFixture<DashboardRecreioTiposTests.SharedWorld>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string EnunciadoA = "<script>alert('xss')</script> Qual é o maior bioma?";
    private const string EnunciadoB = "Pergunta exclusiva da unidade B";

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex ImgTag();

    internal sealed record World(HelinhoWebFactory Factory, Guid UnitA, Guid EmptyUnit);

    /// <summary>
    /// Um host para a classe inteira, com o mundo semeado uma vez; os testes só leem, então a ordem não importa.
    /// Cada host deixa observadores de arquivo (inotify no Linux), e o runner do CI limita a 128 instâncias (#215).
    /// </summary>
    public sealed class SharedWorld : IAsyncDisposable
    {
        private World? _world;

        internal async Task<World> GetAsync(PostgresFixture postgres)
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

    // Unidades A e B, cada uma com 1 aluno que errou 6 vezes uma questão própria (acerto 0%, acima do mínimo de 5
    // respostas). A unidade A tem ainda uma turma parada (3B). Uma terceira unidade fica vazia. Um evento termina em 30 h.
    private static async Task<World> SeedAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Unit a = new("Unidade Tipos A"), b = new("Unidade Tipos B"), empty = new("Unidade Tipos Vazia");
        db.Units.AddRange(a, b, empty);
        var users = new[]
        {
            new AppUser { UserName = "tipos-a", UnitId = a.Id }, new AppUser { UserName = "tipos-b", UnitId = b.Id },
            new AppUser { UserName = "tipos-parado", UnitId = a.Id },
        };
        db.Users.AddRange(users);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Student Novo(AppUser u, Unit unit, string name, string turma) => new(u.Id, unit.Id, $"TIP-{name}", name, "00.000.000-0", "000.000.000-00",
            "Rua Sintética, 1", new DateOnly(2018, 3, 1), "Responsável Fictício", turma, today);
        Student sa = Novo(users[0], a, "Ana", "5A"), sb = Novo(users[1], b, "Caio", "5A");
        db.Students.AddRange(sa, sb, Novo(users[2], a, "Beto", "3B"));
        db.LiveEvents.Add(new LiveEvent("Festival Sintético", "", QuizType.Ambiental, DateTimeOffset.UtcNow.AddHours(30)));
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
        return new World(factory, a.Id, empty.Id);
    }

    private static Guid? Todas(World _) => null;

    private static Guid? UnidadeA(World world) => world.UnitA;

    private async Task<string> GetAsync(string role, Func<World, Guid?> unit) => WebUtility.HtmlDecode(await GetRawAsync(role, unit));

    private async Task<string> GetRawAsync(string role, Func<World, Guid?> unit)
    {
        var world = await shared.GetAsync(postgres);
        using var client = world.Factory.CreateClientAs(role, unit(world));
        var response = await client.GetAsync("/Dashboard", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task Acertos_por_tipo_mostram_os_cinco_temas_com_nome_e_personagem_e_imagens_dimensionadas()
    {
        var html = await GetAsync(RoleNames.Master, Todas);

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
        var raw = await GetRawAsync(RoleNames.Master, Todas);
        var html = WebUtility.HtmlDecode(raw);

        Assert.Contains("Unidades que mais jogam", html, StringComparison.Ordinal);
        Assert.Contains("Unidade Tipos A", html, StringComparison.Ordinal);
        Assert.Contains("Unidade Tipos B", html, StringComparison.Ordinal);
        Assert.Contains(EnunciadoB, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", raw, StringComparison.Ordinal);
        AssertSemBlocosDaPreviewSemDado(html);
    }

    [Fact]
    public async Task Administrador_nao_recebe_o_bloco_de_unidades_nem_dados_da_outra_unidade()
    {
        var raw = await GetRawAsync(RoleNames.Admin, UnidadeA);
        var html = WebUtility.HtmlDecode(raw);

        Assert.DoesNotContain("Unidades que mais jogam", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Unidade Tipos B", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EnunciadoB, html, StringComparison.Ordinal);
        Assert.Contains("Questões que mais confundem", html, StringComparison.Ordinal);
        Assert.Contains("Qual é o maior bioma?", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alerta_de_acerto_baixo_leva_a_rota_que_existe_e_o_botao_revisar_abre_a_questao()
    {
        var html = await GetAsync(RoleNames.Admin, UnidadeA);

        Assert.Contains("Precisa de atenção", html, StringComparison.Ordinal);
        // O título tem de estar dentro do mesmo <a> do destino: a busca não atravessa o </a> para o alerta vizinho.
        Assert.Matches(
            new Regex(
                $"""<a class="attn" href="/Desempenho"><span class="ic ic-q cul"(?:(?!</a>).)*Cultural abaixo de {GetDashboardAttention.LowAccuracyPercent}%""",
                RegexOptions.Singleline),
            html);
        Assert.Matches("""href="/Questoes/Editar/[0-9a-f-]{36}">Revisar""", html);
        Assert.DoesNotContain("href=\"\"", html, StringComparison.Ordinal);
    }

    // Critério 4 da #179: a preview tem "Dia da Água começa sexta", "6 de 10 perguntas prontas" e "Revise antes do
    // evento de sexta", sem dado que os sustente (LiveEvent só tem EndsAt e não se liga a Question). Com evento perto
    // do fim, rascunhos e turma parada no banco, os quatro alertas reais aparecem e nenhum desses textos sai.
    // Administrador da unidade A: o histórico semeado (outras unidades) não disputa o corte de MaxAlerts com o evento.
    [Fact]
    public async Task Painel_nao_mostra_evento_comecando_nem_perguntas_prontas_e_os_alertas_levam_a_rotas_reais()
    {
        var html = await GetAsync(RoleNames.Admin, UnidadeA);

        Assert.Contains("Festival Sintético termina", html, StringComparison.Ordinal);
        Assert.Matches("""\d+ questões? em rascunho""", html);
        AssertSemBlocosDaPreviewSemDado(html);

        Assert.Contains("""<a class="attn" href="/Eventos">""", html, StringComparison.Ordinal);
        Assert.Contains("""<a class="attn" href="/Questoes?status=Draft">""", html, StringComparison.Ordinal);
        Assert.Matches("""<a class="attn" href="/Alunos\?unidade=[0-9a-f-]{36}&turma=3B">""", html);
    }

    // Critério 4 da #179, para os dois papéis: blocos da preview sem dado que os sustente.
    private static void AssertSemBlocosDaPreviewSemDado(string html)
    {
        // Qualquer forma do verbo (começa, começará, começando, comece). O substantivo "começo" fica de fora: o banco
        // de questões do seed tem "no começo do ano" num enunciado que pode entrar em "Questões que mais confundem".
        Assert.DoesNotMatch(new Regex("""\bcome[çc](?!o\b)""", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex("""\d+\s+de\s+\d+\s+perguntas""", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("perguntas prontas", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Revise antes do evento", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Titulo_de_acerto_baixo_sem_tipo_nao_quebra_a_pagina()
    {
        var title = IndexModel.AlertTitle(new AttentionAlert(AttentionKind.LowAccuracy, 40, ""));

        Assert.Equal($"Acerto abaixo de {GetDashboardAttention.LowAccuracyPercent}%", title);
    }

    [Fact]
    public async Task Unidade_sem_respostas_nao_desenha_cartoes_de_tipo()
    {
        var html = await GetRawAsync(RoleNames.Admin, world => world.EmptyUnit);

        Assert.Contains("Nenhuma resposta neste período.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"castcard", html, StringComparison.Ordinal);
    }
}
