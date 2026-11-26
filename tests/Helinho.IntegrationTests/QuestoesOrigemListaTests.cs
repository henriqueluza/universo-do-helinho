using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helinho.IntegrationTests;

/// <summary>Issue #289: selo de origem IA, marca "editada" e filtro Origem na lista de Questões (ADR-0004 §15). Um host para a classe.</summary>
[Collection(PostgresCollection.Name)]
public class QuestoesOrigemListaTests(PostgresFixture postgres, QuestoesOrigemListaTests.SharedFactory shared)
    : IClassFixture<QuestoesOrigemListaTests.SharedFactory>
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly (string, bool)[] Choices = [("A", true), ("B", false), ("C", false), ("D", false)];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class SharedFactory : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        public AiOptions Ai { get; } = new() { Provider = AiOptions.Fake };

        public async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                _factory = new HelinhoWebFactory(postgres, configure: services => services.AddSingleton<IOptions<AiOptions>>(Microsoft.Extensions.Options.Options.Create(Ai)));
                await _factory.EnsureDatabaseAsync();
            }

            return _factory;
        }

        public async ValueTask DisposeAsync() => await (_factory?.DisposeAsync() ?? ValueTask.CompletedTask);
    }

    /// <summary>Banco com as questões zeradas e, para cada rótulo, uma questão: "Equipe", "IA" e "IA editada".</summary>
    private async Task<HelinhoWebFactory> SeedAsync(bool enabled = true, int teamCount = 1)
    {
        shared.Ai.Provider = enabled ? AiOptions.Fake : AiOptions.Off;
        var factory = await shared.GetAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.QuizAnswers.ExecuteDeleteAsync(Ct);
        await db.Questions.ExecuteDeleteAsync(Ct);
        var job = new AiGenerationJob(AiJobKind.Single, QuizType.Cultural, AgeBand.Age8To10, 1, null, Guid.NewGuid(), GeneratedAt);
        db.AiGenerationJobs.Add(job);
        for (var i = 0; i < teamCount; i++)
        {
            db.Questions.Add(new Question(QuizType.Cultural, AgeBand.Age8To10, $"Pergunta da equipe {Letters(i)}?", "Explicação.", Choices));
        }

        db.Questions.Add(Question.FromAi(QuizType.Cultural, AgeBand.Age8To10, "Pergunta sugerida pela IA pura?", "Explicação.", Choices, job.Id, "gerar-questoes.v1", GeneratedAt));
        var edited = Question.FromAi(QuizType.Cultural, AgeBand.Age8To10, "Pergunta sugerida pela IA e editada?", "Explicação.", Choices, job.Id, "gerar-questoes.v1", GeneratedAt);
        edited.Update(QuizType.Cultural, AgeBand.Age8To10, "Pergunta sugerida pela IA e editada?", "Explicação.", Choices, GeneratedAt.AddHours(1));
        db.Questions.Add(edited);
        await db.SaveChangesAsync(Ct);
        return factory;
    }

    private static string Letters(int i) => string.Concat(i.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => (char)('a' + (c - '0')))); // sem dígitos: o enunciado fica só com letras

    private static async Task<string> GetAsync(HelinhoWebFactory factory, string url)
    {
        using var client = factory.CreateClientAs(RoleNames.Admin);
        return WebUtility.HtmlDecode(await client.GetStringAsync(url, Ct));
    }

    private static List<string> Rows(string html) => [.. Regex.Matches(html, "<th scope=\"row\"><div class=\"clip\"[^>]*>([^<]*)</div>").Select(m => m.Groups[1].Value)];

    [Fact]
    public async Task Questao_de_origem_IA_mostra_o_selo_e_a_editada_mostra_tambem_a_marca_em_texto()
    {
        var factory = await SeedAsync();

        var html = await GetAsync(factory, "/Questoes");

        var rows = Regex.Split(html, "<tr>").Skip(2).ToList(); // cabeçalho e linhas; a ordem é por enunciado
        var team = rows.Single(r => r.Contains("da equipe", StringComparison.Ordinal));
        var pure = rows.Single(r => r.Contains("IA pura", StringComparison.Ordinal));
        var edited = rows.Single(r => r.Contains("IA e editada", StringComparison.Ordinal));
        Assert.DoesNotContain("tag-ai", team);
        Assert.Contains("tag tag-ai", pure);
        Assert.Contains("(sugerida pela IA)", pure);
        Assert.DoesNotContain("editada", pure);
        Assert.Contains("IA · editada", edited);
        Assert.Contains("(sugerida pela IA e editada pela equipe)", edited);
        Assert.Contains("<use href=\"#i-ai\"/>", pure);
    }

    [Fact]
    public async Task Filtro_Origem_lista_Todas_Equipe_e_IA_com_Aplicar_sem_onchange_e_filtra_no_servidor()
    {
        var factory = await SeedAsync();

        var all = await GetAsync(factory, "/Questoes");
        var team = await GetAsync(factory, "/Questoes?origem=Team");
        var ai = await GetAsync(factory, "/Questoes?origem=Ai");
        var teamCultural = await GetAsync(factory, "/Questoes?origem=Team&tipo=Cultural");
        var aiOtherType = await GetAsync(factory, "/Questoes?origem=Ai&tipo=Esportivo");

        Assert.Matches("<label for=\"origem\">Origem</label>\\s*<select id=\"origem\" name=\"origem\" class=\"select\">", all);
        Assert.Contains("<option value=\"\">Todas</option>", all);
        Assert.Contains("<option value=\"Team\">Equipe</option>", all);
        Assert.Contains("<option value=\"Ai\">IA</option>", all);
        Assert.Contains(">Aplicar</button>", all);
        Assert.DoesNotContain("onchange", all, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Rows(all).Count);
        Assert.Equal(["Pergunta da equipe a?"], Rows(team));
        Assert.Equal(["Pergunta sugerida pela IA e editada?", "Pergunta sugerida pela IA pura?"], Rows(ai));
        Assert.Contains("<option value=\"Ai\" selected=\"selected\">IA</option>", ai);
        Assert.Equal(["Pergunta da equipe a?"], Rows(teamCultural)); // combinado com E, não com OU: as três questões são Culturais
        Assert.Empty(Rows(aiOtherType)); // há questões de IA, mas nenhuma Esportiva
    }

    [Fact]
    public async Task Valor_invalido_de_origem_e_ignorado_e_a_lista_mostra_todas()
    {
        var factory = await SeedAsync();

        var html = await GetAsync(factory, "/Questoes?origem=Robo");

        Assert.Equal(3, Rows(html).Count);
    }

    [Fact]
    public async Task Com_a_IA_desligada_o_filtro_some_mas_o_selo_continua_e_a_URL_com_origem_ainda_filtra()
    {
        var factory = await SeedAsync(enabled: false);

        var off = await GetAsync(factory, "/Questoes");
        var byUrl = await GetAsync(factory, "/Questoes?origem=Ai");

        Assert.DoesNotContain("id=\"origem\"", off);
        Assert.Contains("tag tag-ai", off);
        Assert.Equal(2, Rows(byUrl).Count);
        Assert.Contains("id=\"origem\"", byUrl);
    }

    [Fact]
    public async Task Origem_vai_junto_na_paginacao_e_no_Aprovar()
    {
        var factory = await SeedAsync(teamCount: 1);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.AiGenerationJobs.FirstAsync(Ct);
            for (var i = 0; i < 20; i++)
            {
                db.Questions.Add(Question.FromAi(QuizType.Cultural, AgeBand.Age8To10, $"Outra pergunta da IA {Letters(i)}?", "Explicação.", Choices, job.Id, "gerar-questoes.v1", GeneratedAt));
            }

            await db.SaveChangesAsync(Ct);
        }

        var html = await GetAsync(factory, "/Questoes?origem=Ai");

        Assert.Contains("rel=\"next\" href=\"/Questoes?origem=Ai&pagina=2\"", html);
        Assert.Matches("action=\"/Questoes\\?[^\"]*origem=Ai", html); // o Aprovar volta para a lista filtrada
    }

    /// <summary>
    /// Três pedidos em lote: o mais antigo de todos só com uma pendente cujo texto a retenção já apagou (não conta nem é
    /// destino do aviso), o seguinte com 1 pendente e 1 descartada, o mais novo com 2 pendentes. Devolve o do meio.
    /// </summary>
    private static async Task<Guid> SeedPendingAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AiQuestionSuggestions.ExecuteDeleteAsync(Ct);
        AiGenerationJob Batch(int hoursAgo) => new(AiJobKind.Batch, QuizType.Cultural, AgeBand.Age8To10, 5, null, Guid.NewGuid(), GeneratedAt.AddHours(-hoursAgo));
        var (purgedOnly, older, newer) = (Batch(9), Batch(5), Batch(1));
        AiQuestionSuggestion Suggestion(AiGenerationJob job, int position) => new(job.Id, position, $"Sugestão pendente {Letters(position)}?", ["A", "B", "C", "D"], 0, "Explicação.");
        var discarded = Suggestion(older, 1);
        discarded.Discard(Guid.NewGuid(), GeneratedAt);
        var purged = Suggestion(purgedOnly, 0);
        db.AiGenerationJobs.AddRange(purgedOnly, older, newer);
        db.AiQuestionSuggestions.AddRange(purged, Suggestion(older, 0), discarded, Suggestion(newer, 0), Suggestion(newer, 1));
        await db.SaveChangesAsync(Ct);
        // Retenção (ADR-0004 D7): a linha continua Pending, mas sem texto não há o que revisar.
        await db.AiQuestionSuggestions.Where(s => s.Id == purged.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContentPurgedAt, (DateTimeOffset?)GeneratedAt), Ct);
        return older.Id;
    }

    private static async Task ClearPendingAsync(HelinhoWebFactory factory, bool keepOne = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keep = keepOne
            ? await db.AiQuestionSuggestions.Where(s => s.Status == AiSuggestionStatus.Pending && s.ContentPurgedAt == null).Select(s => s.Id).FirstAsync(Ct)
            : Guid.Empty;
        await db.AiQuestionSuggestions.Where(s => s.Id != keep).ExecuteDeleteAsync(Ct);
    }

    /// <summary>Issue #300 (ADR-0004 §15): "Gerar com IA" antes de "Nova questão" e o aviso que leva de volta à revisão.</summary>
    [Fact]
    public async Task Com_a_IA_ligada_a_lista_tem_Gerar_com_IA_e_o_aviso_conta_as_pendentes_e_leva_ao_pedido_mais_antigo()
    {
        var factory = await SeedAsync();
        var older = await SeedPendingAsync(factory);

        var many = await GetAsync(factory, "/Questoes");
        await ClearPendingAsync(factory, keepOne: true);
        var one = await GetAsync(factory, "/Questoes");
        await ClearPendingAsync(factory);
        var none = await GetAsync(factory, "/Questoes");

        Assert.Matches("<a class=\"btn btn-ai\" href=\"/Questoes/Ia\"><svg class=\"fill\" aria-hidden=\"true\"><use href=\"#i-ai\"/></svg>Gerar com IA</a>\\s*<a class=\"btn btn-primary\"[^>]*>.*Nova questão", many);
        Assert.Contains("<div class=\"note ai\" id=\"ia-pendentes\">", many);
        Assert.Contains("<b>3 sugestões da IA esperando revisão.</b>", many); // a descartada não conta
        Assert.Contains($"<a class=\"link-btn\" href=\"/Questoes/Ia/{older}\">Revisar agora</a>", many);
        Assert.Contains("<b>1 sugestão da IA esperando revisão.</b>", one);
        Assert.DoesNotContain("ia-pendentes", none);
        Assert.Contains("Gerar com IA", none);
    }

    [Fact]
    public async Task Com_a_IA_desligada_a_lista_nao_tem_Gerar_com_IA_nem_aviso_mesmo_com_sugestoes_pendentes()
    {
        var factory = await SeedAsync(enabled: false);
        await SeedPendingAsync(factory);

        var off = await GetAsync(factory, "/Questoes");
        await ClearPendingAsync(factory);

        Assert.DoesNotContain("btn-ai", off);
        Assert.DoesNotContain("Gerar com IA", off);
        Assert.DoesNotContain("ia-pendentes", off);
        Assert.DoesNotContain("/Questoes/Ia", off);
        Assert.Contains("tag tag-ai", off); // o selo de origem continua (#289)
    }
}
