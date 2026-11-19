using System.Net;
using System.Text.RegularExpressions;
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

    private static async Task SeedAsync(HelinhoWebFactory factory, string statement)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Questions.Add(new Question(QuizType.Esportivo, AgeBand.Age8To10, statement, "Explicação.", [("A", false), ("B", false), ("C", true), ("D", false)]));
        await db.SaveChangesAsync(Ct);
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
        Assert.Contains("1 questões", html, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(html, "<h1"));
        Assert.All(ForaDoEscopo, s => Assert.DoesNotContain(s, html, StringComparison.OrdinalIgnoreCase));
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
    }
}
