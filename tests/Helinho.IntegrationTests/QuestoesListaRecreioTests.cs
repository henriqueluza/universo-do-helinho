using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Lista de Questões no tema Recreio (#162): tema + personagem, situação em texto, sem IA, estado vazio.</summary>
[Collection(PostgresCollection.Name)]
public class QuestoesListaRecreioTests(PostgresFixture postgres)
{
    private static readonly string[] ForaDoEscopo =
    [
        "btn-ai", "tag-ai", "note ai", "ai-help", "ai-filled", "ai-flag", "--ai", "Gerar com IA", "Sugerir",
        "Origem", "Só no evento", "onchange", "<noscript",
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

    private static async Task<Guid> SeedAsync(HelinhoWebFactory factory, string statement, bool approved = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var question = new Question(QuizType.Esportivo, AgeBand.Age8To10, statement, "Explicação.", [("A", false), ("B", false), ("C", true), ("D", false)]);
        if (approved)
        {
            question.Approve();
        }

        db.Questions.Add(question);
        await db.SaveChangesAsync(Ct);
        return question.Id;
    }

    [Fact]
    public async Task Lista_usa_o_tema_Recreio_com_tema_e_personagem_e_sem_IA()
    {
        await using var factory = await NewFactoryAsync();
        await SeedAsync(factory, "Quantos jogadores ficam em quadra?");
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await client.GetStringAsync("/Questoes", Ct);

        Assert.Contains("class=\"page-head\"", html, StringComparison.Ordinal);
        Assert.Contains("filters-bar", html, StringComparison.Ordinal);
        Assert.Contains(">Aplicar</button>", html, StringComparison.Ordinal);
        Assert.Contains("tag tag-type esp", html, StringComparison.Ordinal);
        Assert.Contains("(personagem Max)", html, StringComparison.Ordinal);
        Assert.Matches("<img src=\"[^\"]*cara-max\\.webp\" width=\"296\" height=\"320\" alt=\"\"", html);
        Assert.Contains("tag tag-draft\">Rascunho</span>", html, StringComparison.Ordinal);
        Assert.Contains("1 questão · página 1 de 1", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Paginação\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(html, "<h1"));
        Assert.All(ForaDoEscopo, s => Assert.DoesNotContain(s, html, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Linha_tem_Editar_em_toda_questao_e_Aprovar_so_no_rascunho()
    {
        await using var factory = await NewFactoryAsync();
        var rascunho = await SeedAsync(factory, "Qual esporte usa uma rede baixa e peteca?");
        var aprovada = await SeedAsync(factory, "Qual esporte usa raquete e bolinha amarela?", approved: true);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes", Ct));

        Assert.Contains($"class=\"link-btn\" href=\"/Questoes/Editar/{rascunho}\">Editar", html, StringComparison.Ordinal);
        Assert.Contains($"class=\"link-btn\" href=\"/Questoes/Editar/{aprovada}\">Editar", html, StringComparison.Ordinal);
        var aprovar = Regex.Matches(html, "<form method=\"post\" action=\"([^\"]*handler=Aprovar[^\"]*)\">(.*?)</form>", RegexOptions.Singleline);
        var unico = Assert.Single(aprovar);
        Assert.Contains($"id={rascunho}", unico.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", unico.Groups[2].Value, StringComparison.Ordinal);
        Assert.Contains("type=\"submit\">Aprovar<span class=\"visually-hidden\"> questão: Qual esporte usa uma rede baixa e peteca?</span>", unico.Groups[2].Value, StringComparison.Ordinal);
        Assert.DoesNotContain(aprovada.ToString(), unico.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("<div class=\"clip\" title=\"Qual esporte usa uma rede baixa e peteca?\">Qual esporte usa uma rede baixa e peteca?</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lista_vazia_mostra_estado_vazio_com_acao()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes", Ct));

        Assert.Contains("id=\"questoes-vazio\"", html, StringComparison.Ordinal);
        Assert.Contains("Nova questão", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
        var vazio = EmptyState(html);
        Assert.Matches("<img src=\"[^\"]*helinho-corpo\\.webp\" width=\"182\" height=\"360\" alt=\"\"", vazio);
        Assert.Matches("<a class=\"btn\" href=\"/Questoes/Editar\">.*Nova questão</a>", vazio);
        Assert.Equal(1, Regex.Count(html, "btn btn-primary"));
    }

    [Fact]
    public async Task Lista_filtrada_sem_resultado_oferece_limpar_filtros()
    {
        await using var factory = await NewFactoryAsync();
        await SeedAsync(factory, "Quantos jogadores ficam em quadra?");
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes?tipo=Diversidade", Ct));

        var vazio = EmptyState(html);
        Assert.Contains("<b>Nenhuma questão encontrada.</b>", vazio, StringComparison.Ordinal);
        Assert.Matches("<img src=\"[^\"]*helinho-corpo\\.webp\" width=\"182\" height=\"360\" alt=\"\"", vazio);
        Assert.Contains("<a class=\"btn\" href=\"/Questoes\">Limpar filtros</a>", vazio, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova questão", vazio, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paginacao_aparece_so_com_mais_de_uma_pagina()
    {
        await using var factory = await NewFactoryAsync();
        for (var i = 0; i <= ListQuestions.PageSize; i++)
        {
            await SeedAsync(factory, $"Questão sintética de paginação {i}?");
        }

        using var client = factory.CreateClientAs(RoleNames.Master);

        var primeira = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes", Ct));
        var segunda = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes?pagina=2", Ct));

        Assert.Contains($"{ListQuestions.PageSize + 1} questões · página 1 de 2", primeira, StringComparison.Ordinal);
        Assert.Contains("<nav aria-label=\"Paginação\">", primeira, StringComparison.Ordinal);
        Assert.Contains("rel=\"next\"", primeira, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"prev\"", primeira, StringComparison.Ordinal);
        Assert.Contains("rel=\"prev\"", segunda, StringComparison.Ordinal);
        Assert.DoesNotContain("rel=\"next\"", segunda, StringComparison.Ordinal);
    }

    private static string EmptyState(string html)
    {
        var match = Regex.Match(html, "<section [^>]*id=\"questoes-vazio\"[^>]*>(.*?)</section>", RegexOptions.Singleline);
        Assert.True(match.Success, "estado vazio #questoes-vazio não encontrado");
        return match.Groups[1].Value;
    }
}
