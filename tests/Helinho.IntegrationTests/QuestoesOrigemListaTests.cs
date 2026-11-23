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
}
