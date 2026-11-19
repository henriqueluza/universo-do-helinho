using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Eventos no tema Recreio (#173): classes-chave, tema com personagem, imagens e erros acessíveis.</summary>
[Collection(PostgresCollection.Name)]
public class EventosRecreioTests(PostgresFixture postgres)
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
    public async Task Eventos_mostram_tema_com_personagem_e_radios_do_modelo_sem_JavaScript()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.LiveEvents.ExecuteDeleteAsync(Ct);
            db.LiveEvents.Add(new LiveEvent("Festival Recreio", "", QuizType.Ambiental, new DateTimeOffset(2090, 6, 15, 13, 0, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());
        var html = await client.GetStringAsync("/Eventos", Ct);

        Assert.Contains("<div class=\"table-wrap\">", html);
        Assert.Contains("tag tag-type amb", html);
        Assert.Contains("alt=\"Flora\"", html);
        Assert.Contains("class=\"panel panel-pad form\"", html);
        Assert.Contains("<fieldset", html);
        Assert.Contains("<legend", html);
        Assert.Equal(5, Regex.Count(html, "type=\"radio\" name=\"Input.QuizType\""));
        Assert.Equal(5, Regex.Count(html, "<img src=\"/img/cara-[a-z]+.webp\" width=\"\\d+\" height=\"\\d+\" alt=\"\""));
        Assert.DoesNotContain("<select", html);
        Assert.Single(Regex.Matches(html, "<h1"));
        NoLegacyNoAi(html);
    }

    [Fact]
    public async Task Evento_com_erro_mostra_resumo_e_marca_os_radios()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);
        var page = await client.GetStringAsync("/Eventos", Ct);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/Eventos", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Title"] = "",
            ["Input.EndsAt"] = "2090-06-15T10:00",
        }), Ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Contains("id=\"resumo-erros\"", html);
        Assert.Contains("href=\"#Input_QuizType\"", html);
        Assert.Contains("id=\"Input_QuizType\"", html);
        Assert.Contains("id=\"Input_QuizType-erro\" class=\"field-error\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
    }
}
