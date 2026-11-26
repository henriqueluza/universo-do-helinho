using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286: aprovar e descartar sugestões em /questoes/ia/{id} (ADR-0004 §9.3). Arquivo da segunda entrega (Issue #303).</summary>
public partial class AiTelaRevisarTests
{
    private static Task<HttpResponseMessage> ActAsync(HttpClient client, Guid job, string handler, Guid suggestion) =>
        PostAsync(client, $"/Questoes/Ia/{job}", $"/Questoes/Ia/{job}?handler={handler}&id={suggestion}");

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
    public async Task Editar_sugestao_ja_revisada_volta_ao_pedido_com_aviso()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);
        var (job, ids) = await SeedAsync(factory);
        using var done = await ActAsync(client, job, "Descartar", ids[1]);

        using var response = await client.GetAsync($"/Questoes/Ia/{job}/editar/{ids[1]}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Questoes/Ia/{job}", response.Headers.Location!.OriginalString);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location, Ct));
        Assert.Contains("Esta sugestão já foi revisada.", html, StringComparison.Ordinal);
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
