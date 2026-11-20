using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Missões no tema Recreio (#155): classes-chave, tema com personagem, imagens e erros acessíveis.</summary>
[Collection(PostgresCollection.Name)]
public class MissoesRecreioTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void NoLegacyNoAi(string html)
    {
        Assert.DoesNotContain("card-panel", html);
        Assert.DoesNotContain("btn-ai", html);
        Assert.DoesNotContain("tag-ai", html);
        Assert.DoesNotContain("onchange", html);
        Assert.DoesNotContain("<noscript", html);
    }


    [Fact]
    public async Task Lista_de_missoes_usa_table_wrap_tema_com_personagem_e_imagem_com_dimensoes()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(Ct);
            db.DailyQuests.Add(new DailyQuest($"Recreio-{Guid.NewGuid():N}", 4, 40, QuizType.Esportivo));
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var html = await client.GetStringAsync("/Missoes", Ct);

        Assert.Contains("class=\"page-head\"", html);
        Assert.Contains("<div class=\"table-wrap\">", html);
        Assert.Contains("tag tag-type esp", html);
        Assert.Contains("alt=\"Max\"", html);
        Assert.Contains("<img src=\"/img/cara-max.webp\" width=\"296\" height=\"320\" alt=\"Max\"", html);
        Assert.Contains("<th scope=\"col\" class=\"actions\">Ações</th>", html);
        NoLegacyNoAi(html);
    }

    [Fact]
    public async Task Edicao_de_missao_usa_form_do_Recreio_e_erro_acessivel()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(Ct);
            var quest = new DailyQuest("Editável", 3, 20, null);
            db.DailyQuests.Add(quest);
            await db.SaveChangesAsync(Ct);
            id = quest.Id;
        }

        using var client = factory.CreateClientAs(RoleNames.Master);
        var page = await client.GetStringAsync($"/Missoes/Editar/{id}", Ct);
        Assert.Contains("class=\"panel panel-pad form\"", page);
        NoLegacyNoAi(page);

        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync($"/Missoes/Editar/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Title"] = "",
            ["Input.Target"] = "0",
            ["Input.RewardXp"] = "20",
        }), Ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"resumo-erros\"", html);
        Assert.Contains("role=\"alert\" tabindex=\"-1\"", html);
        Assert.Contains("aria-describedby=\"Input_Title-erro\"", html);
        Assert.Contains("id=\"Input_Target-erro\" class=\"field-error\"", html);
    }
}
