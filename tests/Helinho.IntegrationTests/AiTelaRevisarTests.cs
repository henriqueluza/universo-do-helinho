using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286, fatia 3: revisar as sugestões (aprovar, editar, descartar) em /questoes/ia/{id}. Só o gerador Fake.</summary>
[Collection(PostgresCollection.Name)]
public partial class AiTelaRevisarTests(PostgresFixture postgres)
{
    private const string Xss = "<script>alert(1)</script>";
    private static readonly string[] Four = ["Um", "Dois", "Três", "Quatro"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres, configure: services => services.Configure<AiOptions>(o => o.Provider = AiOptions.Fake));
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    /// <summary>Um pedido concluído com <paramref name="count"/> sugestões pendentes (a primeira leva o texto malicioso).</summary>
    private static async Task<(Guid Job, List<Guid> Suggestions)> SeedAsync(HelinhoWebFactory factory, int count = 3)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AiGenerationJob(AiJobKind.Batch, QuizType.Esportivo, AgeBand.Age8To10, count, null, Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-30));
        job.Succeed(new AiJobUsage("Fake", "fake-v1", "gerar-questoes.v1", 10, 20, TimeSpan.FromSeconds(1)), new AiJobCounts(count, count, 0, 0), DateTimeOffset.UtcNow);
        db.AiGenerationJobs.Add(job);
        var items = Enumerable.Range(0, count)
            .Select(i => new AiQuestionSuggestion(job.Id, i, i == 0 ? $"Sugestão {i} {Xss}?" : $"Sugestão sintética número {i}?", Four, 1, "Explicação sintética."))
            .ToList();
        db.AiQuestionSuggestions.AddRange(items);
        await db.SaveChangesAsync(Ct);
        return (job.Id, [.. items.Select(s => s.Id)]);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string url, params (string Key, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(page, Ct)).Groups[1].Value;
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(form), Ct);
    }

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, Guid job, string handler, Guid suggestion) =>
        PostAsync(client, $"/Questoes/Ia/{job}", $"/Questoes/Ia/{job}?handler={handler}&id={suggestion}");

    private static (string, string)[] Edit(string statement = "Enunciado editado?", string? optionB = "Dois") =>
    [
        ("Input.QuizType", "Cultural"), ("Input.AgeBand", "Age11To12"), ("Input.Statement", statement),
        ("Input.OptionA", "Um"), ("Input.OptionB", optionB ?? ""), ("Input.OptionC", "Três"), ("Input.OptionD", "Quatro"),
        ("Input.Correct", "2"), ("Input.Explanation", "Explicação editada."),
    ];

    private static async Task<(List<Question> Questions, List<AiQuestionSuggestion> Suggestions)> ReadAsync(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Questions.AsNoTracking().Where(q => q.Origin == QuestionOrigin.Ai).ToListAsync(Ct),
            await db.AiQuestionSuggestions.AsNoTracking().OrderBy(s => s.Position).ToListAsync(Ct));
    }

    [Fact]
    public async Task Cartao_pendente_tem_aprovar_editar_e_descartar_com_o_texto_codificado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var (job, _) = await SeedAsync(factory);

        var raw = await client.GetStringAsync($"/Questoes/Ia/{job}", Ct);

        Assert.Equal(3, Regex.Count(raw, "handler=Aprovar"));
        Assert.Equal(3, Regex.Count(raw, "handler=Descartar"));
        Assert.Equal(3, Regex.Count(raw, "href=\"/Questoes/Ia/[0-9a-f-]{36}/editar/"));
        Assert.Equal(7, Regex.Count(raw, "name=\"__RequestVerificationToken\"")); // 6 decisões + o "Sair" do menu
        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("0 de 3 revisadas", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("ia-tudo-revisado", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pedir_5_aprovar_2_editar_1_descartar_2_so_as_aprovadas_ficam_aprovadas()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory, 5);

        using var a1 = await ActAsync(client, job, "Aprovar", ids[0]);
        using var a2 = await ActAsync(client, job, "Aprovar", ids[1]);
        using var d1 = await ActAsync(client, job, "Descartar", ids[3]);
        using var d2 = await ActAsync(client, job, "Descartar", ids[4]);
        using var e1 = await PostAsync(client, $"/Questoes/Ia/{job}/editar/{ids[2]}", $"/Questoes/Ia/{job}/editar/{ids[2]}", Edit());

        Assert.All(new[] { a1, a2, d1, d2, e1 }, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.Equal($"/Questoes/Ia/{job}", e1.Headers.Location!.OriginalString);

        var (questions, suggestions) = await ReadAsync(factory);
        Assert.Equal(2, questions.Count(q => q.Status == QuestionStatus.Approved));
        var draft = Assert.Single(questions, q => q.Status == QuestionStatus.Draft);
        Assert.Equal((QuizType.Cultural, AgeBand.Age11To12, "Enunciado editado?"), (draft.QuizType, draft.AgeBand, draft.Statement));
        Assert.Equal(
            [AiSuggestionStatus.Approved, AiSuggestionStatus.Approved, AiSuggestionStatus.Edited, AiSuggestionStatus.Discarded, AiSuggestionStatus.Discarded],
            suggestions.Select(s => s.Status));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Questoes/Ia/{job}", Ct));
        Assert.Contains("5 de 5 revisadas", html, StringComparison.Ordinal);
        Assert.Contains("Tudo revisado.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("rcard-actions", html, StringComparison.Ordinal);
        Assert.Contains("Descartada", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resultado_da_decisao_aparece_uma_vez_e_decidir_de_novo_avisa_que_ja_foi_revisada()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);

        using var first = await ActAsync(client, job, "Aprovar", ids[1]);
        var shown = WebUtility.HtmlDecode(await client.GetStringAsync(first.Headers.Location!, Ct));
        var gone = await client.GetStringAsync($"/Questoes/Ia/{job}", Ct);
        using var again = await ActAsync(client, job, "Aprovar", ids[1]);
        var warned = WebUtility.HtmlDecode(await client.GetStringAsync(again.Headers.Location!, Ct));

        Assert.Contains("Sugestão aprovada. A questão já pode chegar ao jogo.", shown, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" id=\"ia-resultado\"", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("ia-resultado", gone, StringComparison.Ordinal);
        Assert.Contains("Esta sugestão já foi revisada.", warned, StringComparison.Ordinal);
        Assert.Single((await ReadAsync(factory)).Questions);
    }

    [Fact]
    public async Task Aprovar_duplicata_avisa_e_nao_cria_a_questao()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory, 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = new Question(QuizType.Esportivo, AgeBand.Age8To10, "Sugestão sintética número 1?", "Explicação.", [("A", true), ("B", false), ("C", false), ("D", false)]);
            existing.Approve();
            db.Questions.Add(existing);
            await db.SaveChangesAsync(Ct);
        }

        using var response = await ActAsync(client, job, "Aprovar", ids[1]);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!, Ct));

        Assert.Contains("Já existe uma questão com este enunciado neste tipo e faixa.", html, StringComparison.Ordinal);
        Assert.Empty((await ReadAsync(factory)).Questions);
        Assert.Equal(AiSuggestionStatus.Pending, (await ReadAsync(factory)).Suggestions[1].Status);
    }

    [Fact]
    public async Task Editar_abre_preenchido_sem_gravar_e_codifica_o_texto_da_IA()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);

        var raw = await client.GetStringAsync($"/Questoes/Ia/{job}/editar/{ids[0]}", Ct);

        Assert.DoesNotContain("<script>alert", raw, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw, StringComparison.Ordinal);
        Assert.Contains("Sugerida pela IA", raw, StringComparison.Ordinal);
        Assert.Contains("value=\"Dois\"", raw, StringComparison.Ordinal);
        Assert.Matches("value=\"1\" checked", raw);
        Assert.Equal(4, Regex.Count(raw, "<label class=\"visually-hidden\" for=\"Input_Option"));
        Assert.Empty((await ReadAsync(factory)).Questions);
        Assert.All((await ReadAsync(factory)).Suggestions, s => Assert.Equal(AiSuggestionStatus.Pending, s.Status));
    }

    [Fact]
    public async Task Editar_com_erro_mostra_o_resumo_mantem_o_digitado_e_nao_salva()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);
        var url = $"/Questoes/Ia/{job}/editar/{ids[1]}";

        using var response = await PostAsync(client, url, url, Edit("Texto que fica", optionB: ""));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("Informe a alternativa B.", html, StringComparison.Ordinal);
        Assert.Contains("Texto que fica", html, StringComparison.Ordinal);
        Assert.Empty((await ReadAsync(factory)).Questions);
    }

    [Fact]
    public async Task Editar_sugestao_ja_revisada_volta_ao_pedido_com_aviso()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);
        using var done = await ActAsync(client, job, "Descartar", ids[1]);

        using var response = await client.GetAsync($"/Questoes/Ia/{job}/editar/{ids[1]}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Questoes/Ia/{job}", response.Headers.Location!.OriginalString);
    }

    /// <summary>Issue #299: o id da sugestão vem do formulário; só vale dentro do pedido da URL.</summary>
    [Fact]
    public async Task Sugestao_de_outro_pedido_responde_404_e_nada_e_decidido()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory, 1);
        var (other, _) = await SeedAsync(factory, 1);

        using var approve = await ActAsync(client, other, "Aprovar", ids[0]);
        using var discard = await ActAsync(client, other, "Descartar", ids[0]);
        using var edit = await PostAsync(client, $"/Questoes/Ia/{job}/editar/{ids[0]}", $"/Questoes/Ia/{other}/editar/{ids[0]}", Edit());

        Assert.All(new[] { approve, discard, edit }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        var (questions, suggestions) = await ReadAsync(factory);
        Assert.Empty(questions);
        Assert.All(suggestions, s => Assert.Equal(AiSuggestionStatus.Pending, s.Status));
    }

    [Fact]
    public async Task Decisao_sem_antiforgery_e_recusada()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory, 1);
        using var noToken = await client.PostAsync($"/Questoes/Ia/{job}?handler=Aprovar&id={ids[0]}", new FormUrlEncodedContent([]), Ct);


        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Empty((await ReadAsync(factory)).Questions);
    }
}
