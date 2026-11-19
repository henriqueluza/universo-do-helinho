using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Questões no tema Recreio (#154): lista e criador em 3 passos, sem IA, sem JavaScript, com a prévia no servidor.</summary>
[Collection(PostgresCollection.Name)]
public class QuestoesRecreioTests(PostgresFixture postgres)
{
    private const string Xss = "<script>alert(1)</script>";

    private static readonly string[] ForaDoEscopo =
    [
        "btn-ai", "tag-ai", "note ai", "ai-help", "ai-filled", "ai-flag", "--ai", "Gerar com IA", "Sugerir",
        "Origem", "Onde aparece", "Só no evento", "onchange", "<noscript", "data-sugerir",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.QuizAnswers.ExecuteDeleteAsync(Ct);
        await db.Questions.ExecuteDeleteAsync(Ct);
        return factory;
    }

    private static async Task<Guid> SeedAsync(HelinhoWebFactory factory, string statement, bool approved)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = new Question(QuizType.Esportivo, AgeBand.Age8To10, statement, "Explicação.", [("A", false), ("B", false), ("C", true), ("D", false)]);
        if (approved)
        {
            q.Approve();
        }

        db.Questions.Add(q);
        await db.SaveChangesAsync(Ct);
        return q.Id;
    }

    private static async Task<string> GetAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url, Ct));

    [Fact]
    public async Task Lista_usa_o_tema_Recreio_com_tema_e_personagem_e_sem_IA()
    {
        await using var factory = await NewFactoryAsync();
        await SeedAsync(factory, "Quantos jogadores ficam em quadra?", approved: false);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await client.GetStringAsync("/Questoes", Ct);

        Assert.Contains("class=\"page-head\"", html, StringComparison.Ordinal);
        Assert.Contains("filters-bar", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.Contains("tag tag-type esp", html, StringComparison.Ordinal);
        Assert.Contains("(personagem Max)", html, StringComparison.Ordinal);
        Assert.Matches("<img src=\"[^\"]*cara-max\\.webp\" width=\"296\" height=\"320\" alt=\"\"", html);
        Assert.Contains("tag tag-draft\">Rascunho</span>", html, StringComparison.Ordinal);
        Assert.Contains("1 questões", html, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(html, "<h1"));
        Assert.All(ForaDoEscopo, s => Assert.DoesNotContain(s, html, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Lista_vazia_mostra_estado_vazio_com_acao()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await GetAsync(client, "/Questoes");

        Assert.Contains("id=\"questoes-vazio\"", html, StringComparison.Ordinal);
        Assert.Contains("Nova questão", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Criador_novo_e_um_formulario_de_tres_passos_sem_JavaScript_e_sem_IA()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await GetAsync(client, "/Questoes/Editar");

        var criador = html[html.IndexOf("<div class=\"maker\"", StringComparison.Ordinal)..];
        Assert.Equal(1, Regex.Count(criador, "<form[ >]"));
        Assert.Contains("<form class=\"maker-form\" method=\"post\"", criador, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Count(html, "<fieldset class=\"step\""));
        Assert.Equal(5, Regex.Count(html, "name=\"Input\\.QuizType\""));
        Assert.Equal(4, Regex.Count(html, "name=\"Input\\.AgeBand\""));
        Assert.Equal(4, Regex.Count(html, "name=\"Input\\.Correct\""));
        foreach (var par in new[] { "Ambiental</b>", "Flora</small>", "Conhecimento</b>", "Byte</small>", "Cultural</b>", "Luna</small>", "Esportivo</b>", "Max</small>", "Diversidade</b>", "Lia</small>", "13–14 anos" })
        {
            Assert.Contains(par, html, StringComparison.Ordinal);
        }

        Assert.Contains("class=\"theme-pick\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"pill-pick\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"maker-bar\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"phone \" aria-hidden=\"true\"", html, StringComparison.Ordinal);
        Assert.Matches("<img [^>]*width=\"\\d+\" height=\"\\d+\" alt=\"\"", html);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
        Assert.All(ForaDoEscopo, s => Assert.DoesNotContain(s, html, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Criador_com_erro_mantem_o_que_foi_enviado_e_expoe_os_erros_de_forma_acessivel()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);
        var token = Regex.Match(await client.GetStringAsync("/Questoes/Editar", Ct), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.QuizType"] = "Cultural",
            ["Input.Statement"] = Xss,
            ["Input.OptionA"] = "Um",
            ["Input.OptionC"] = "Três",
        };

        var response = await client.PostAsync("/Questoes/Editar", new FormUrlEncodedContent(fields), Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        var html = WebUtility.HtmlDecode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("id=\"resumo-erros\"[^>]*role=\"alert\"|role=\"alert\"[^>]*id=\"resumo-erros\"", html);
        Assert.Contains("tabindex=\"-1\"", html, StringComparison.Ordinal);
        Assert.Contains("Escolha a faixa etária.", html, StringComparison.Ordinal);
        Assert.Contains("Informe a alternativa B.", html, StringComparison.Ordinal);
        Assert.Contains("Marque a alternativa correta.", html, StringComparison.Ordinal);
        Assert.Matches("id=\"Input_OptionB\"[^>]*aria-invalid=\"true\"[^>]*aria-describedby=\"Input_OptionB-erro\"", html);
        Assert.Contains("id=\"Input_OptionB-erro\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"Input_AgeBand\"[^>]*aria-invalid=\"true\"[^>]*aria-describedby=\"Input_AgeBand-erro\"", html);
        Assert.Contains("checked=\"checked\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"phone cul\"", html, StringComparison.Ordinal);
        // texto de questão sai sempre codificado, na prévia e no campo
        Assert.DoesNotContain(Xss, raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editar_aprovada_mostra_o_conteudo_salvo_na_previa_e_avisa_que_volta_a_rascunho()
    {
        await using var factory = await NewFactoryAsync();
        var id = await SeedAsync(factory, "Quantos jogadores ficam em quadra?", approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await GetAsync(client, $"/Questoes/Editar/{id}");

        Assert.Contains("volta para rascunho", html, StringComparison.Ordinal);
        Assert.Matches("value=\"Esportivo\"[^>]*checked=\"checked\"", html);
        Assert.Matches("value=\"Age8To10\"[^>]*checked=\"checked\"", html);
        Assert.Matches("value=\"2\"[^>]*checked=\"checked\"", html);
        Assert.Contains("class=\"phone esp\"", html, StringComparison.Ordinal);
        Assert.Contains("<li data-k=\"C\" class=\" right\">", html, StringComparison.Ordinal);
        Assert.Contains("Quantos jogadores ficam em quadra?", html, StringComparison.Ordinal);
    }
}
