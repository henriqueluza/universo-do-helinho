using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Criador de Questões no tema Recreio (#154): três passos, sem IA, sem JavaScript, com a prévia no servidor.</summary>
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
    public async Task Criador_novo_e_um_formulario_de_tres_passos_sem_JavaScript_e_sem_IA()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await GetAsync(client, "/Questoes/Editar");

        var inicio = html.IndexOf("<div class=\"maker\"", StringComparison.Ordinal);
        var criador = html[inicio..html.IndexOf("</main>", inicio, StringComparison.Ordinal)];
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
        Assert.Matches("class=\"phone\\s*\" aria-hidden=\"true\"", html);
        Assert.Matches("<img [^>]*width=\"\\d+\" height=\"\\d+\" alt=\"\"", html);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
        Assert.All(ForaDoEscopo, s => Assert.DoesNotContain(s, criador, StringComparison.OrdinalIgnoreCase));
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
        Assert.Matches("value=\"Cultural\"[^>]*checked=\"checked\"", html);
        Assert.Matches("id=\"Input_OptionA\"[^>]*value=\"Um\"", html);
        Assert.Contains("class=\"phone cul\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Xss, raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Criador_recusa_enunciado_acima_de_500_e_alternativa_acima_de_300_caracteres_sem_salvar()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);
        var form = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes/Editar", Ct));
        Assert.Matches("<textarea [^>]*maxlength=\"500\"[^>]*id=\"Input_Statement\"|<textarea [^>]*id=\"Input_Statement\"[^>]*maxlength=\"500\"", form);
        Assert.Matches("id=\"Input_OptionA\"[^>]*maxlength=\"300\"", form);
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.QuizType"] = "Cultural",
            ["Input.AgeBand"] = "Age8To10",
            ["Input.Statement"] = new string('e', 501),
            ["Input.OptionA"] = new string('a', 301),
            ["Input.OptionB"] = "Dois",
            ["Input.OptionC"] = "Três",
            ["Input.OptionD"] = "Quatro",
            ["Input.Correct"] = "1",
            ["Input.Explanation"] = "Explicação.",
        };

        var response = await client.PostAsync("/Questoes/Editar", new FormUrlEncodedContent(fields), Ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("O enunciado deve ter até 500 caracteres.", html, StringComparison.Ordinal);
        Assert.Contains("A alternativa A deve ter até 300 caracteres.", html, StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Questions.CountAsync(Ct));
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
        Assert.Matches("<li data-k=\"C\" class=\"\\s*right\">", html);
        Assert.Contains("Quantos jogadores ficam em quadra?", html, StringComparison.Ordinal);
    }

    // Issue #160: os ganchos do painel.js são só atributos; sem JavaScript a tela fica igual à de #154.
    [Fact]
    public async Task Criador_emite_os_ganchos_da_previa_ao_vivo_sem_mudar_a_tela_sem_JavaScript()
    {
        await using var factory = await NewFactoryAsync();
        var id = await SeedAsync(factory, "Quantos jogadores ficam em quadra?", approved: false);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var nova = await GetAsync(client, "/Questoes/Editar");
        var edicao = await GetAsync(client, $"/Questoes/Editar/{id}");

        foreach (var html in new[] { nova, edicao })
        {
            Assert.Matches("<div class=\"maker\" data-maker>", html);
            Assert.Matches("class=\"phone[^\"]*\" aria-hidden=\"true\" data-preview>", html);
            Assert.Equal(5, Regex.Count(html, "name=\"Input\\.QuizType\"[^>]*data-k=\"\\w+\""));
            Assert.Contains("data-group=\"age\"", html, StringComparison.Ordinal);
            Assert.Matches("<textarea [^>]*data-bind=\"q\"", html);
            Assert.Matches("<textarea [^>]*data-bind=\"why\"", html);
            foreach (var i in new[] { 0, 1, 2, 3 })
            {
                Assert.Contains($"data-bind=\"a{i}\"", html, StringComparison.Ordinal);
                Assert.Contains($"data-show=\"a{i}\"", html, StringComparison.Ordinal);
                Assert.Contains($"data-alt=\"{i}\"", html, StringComparison.Ordinal);
                Assert.Contains($"data-empty=\"Alternativa {"ABCD"[i]}\"", html, StringComparison.Ordinal);
            }

            Assert.Matches("data-show=\"q\" data-empty=\"Sua pergunta aparece aqui\"", html);
            Assert.Contains("<small>Até 500 caracteres.<span class=\"count-hint\" data-count-for=\"Input_Statement\" hidden></span></small>", html, StringComparison.Ordinal);
            Assert.Matches("<span data-ready-hint></span>Fica como rascunho até ser aprovada\\.", html);
            Assert.Equal(5, Regex.Count(html, "<li class=\"(ok)?\" data-check=\"(tema|age|q|alts|right)\""));
            Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
        }

        Assert.Matches("class=\"phone\\s*\" aria-hidden=\"true\" data-preview>", nova);
        Assert.Contains("class=\"phone esp\" aria-hidden=\"true\" data-preview>", edicao, StringComparison.Ordinal);

        // Nova: tudo por fazer, e o que não tem conteúdo some pelo CSS (:empty).
        Assert.Equal(5, Regex.Count(nova, "data-check-text> \\(falta\\)</span>"));
        Assert.DoesNotContain("(feito)", nova, StringComparison.Ordinal);
        Assert.Matches("<span class=\"lvl\" data-show=\"age\"></span>", nova);
        Assert.Matches("<p class=\"gq-why\" data-show=\"why\" data-max=\"160\"></p>", nova);
        Assert.DoesNotContain("data-tick", nova, StringComparison.Ordinal);
        Assert.DoesNotContain("data-face", nova, StringComparison.Ordinal);

        // Edição: tudo feito, conteúdo preenchido e o ✓ só na alternativa certa.
        Assert.Equal(5, Regex.Count(edicao, "data-check-text> \\(feito\\)</span>"));
        Assert.DoesNotContain("(falta)", edicao, StringComparison.Ordinal);
        Assert.Matches("<span class=\"lvl\" data-show=\"age\">[^<]+</span>", edicao);
        Assert.Matches("<p class=\"gq-why\" data-show=\"why\" data-max=\"160\">Explicação\\.</p>", edicao);
        Assert.Equal(1, Regex.Count(edicao, "data-tick"));
        Assert.Matches("<li data-k=\"C\" class=\"right\">\\s*<span[^>]*data-show=\"a2\"[^>]*>[^<]*</span>\\s*<span data-tick>\\s*✓\\s*</span>", edicao);
        Assert.Contains("data-face", edicao, StringComparison.Ordinal);
    }
}
