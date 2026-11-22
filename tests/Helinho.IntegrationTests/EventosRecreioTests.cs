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
public class EventosRecreioTests(PostgresFixture postgres, EventosRecreioTests.SharedHost shared)
    : IClassFixture<EventosRecreioTests.SharedHost>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Um host para os três testes da classe: o primeiro troca os eventos do banco e os outros dois só postam formulários
    /// inválidos, que não gravam nada. Cada host deixa observadores de arquivo (inotify no Linux), e o runner do CI
    /// limita a 128 instâncias (#215).
    /// </summary>
    public sealed class SharedHost : IAsyncDisposable
    {
        private HelinhoWebFactory? _factory;

        internal async Task<HelinhoWebFactory> GetAsync(PostgresFixture postgres)
        {
            if (_factory is null)
            {
                var factory = new HelinhoWebFactory(postgres);
                await factory.EnsureDatabaseAsync();
                _factory = factory;
            }

            return _factory;
        }

        public async ValueTask DisposeAsync()
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
    }

    private static void NoLegacyNoAi(string html)
    {
        Assert.DoesNotContain("card-panel", html);
        Assert.DoesNotContain("btn-ai", html);
        Assert.DoesNotContain("tag-ai", html);
        Assert.DoesNotContain("onchange", html);
        Assert.DoesNotContain("<noscript", html);
    }

    private static async Task<string> PostEventoAsync(HttpClient client, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync("/Eventos", Ct);
        fields["__RequestVerificationToken"] = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/Eventos", new FormUrlEncodedContent(fields), Ct);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Eventos_mostram_tema_com_personagem_e_radios_do_modelo_sem_JavaScript()
    {
        var factory = await shared.GetAsync(postgres);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.LiveEvents.ExecuteDeleteAsync(Ct);
            db.LiveEvents.Add(new LiveEvent("Festival Recreio", "", QuizType.Ambiental, new DateTimeOffset(2090, 6, 15, 13, 0, 0, TimeSpan.Zero)));
            await db.SaveChangesAsync(Ct);
        }

        using var client = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());
        var html = await client.GetStringAsync("/Eventos", Ct);

        Assert.Contains("<div class=\"page-head\">", html);
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
        var factory = await shared.GetAsync(postgres);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await PostEventoAsync(client, new Dictionary<string, string>
        {
            ["Input.Title"] = "",
            ["Input.EndsAt"] = "2090-06-15T10:00",
        });

        Assert.Contains("id=\"resumo-erros\"", html);
        Assert.Contains("href=\"#Input_QuizType\"", html);
        Assert.Contains("id=\"Input_QuizType\"", html);
        Assert.Contains("id=\"Input_QuizType-erro\" class=\"field-error\"", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Contains("aria-describedby=\"Input_Title-erro\"", html);
        Assert.Contains("id=\"Input_Title-erro\" class=\"field-error\"", html);
        Assert.Contains("aria-describedby=\"Input_EndsAt-ajuda\"", html);
        Assert.DoesNotContain("Input_EndsAt-erro", html);
        Assert.Contains("<fieldset class=\"field full\" aria-describedby=\"Input_QuizType-erro\">", html);
    }

    [Fact]
    public async Task Evento_sem_termino_liga_ajuda_e_erro_no_aria_describedby()
    {
        var factory = await shared.GetAsync(postgres);
        using var client = factory.CreateClientAs(RoleNames.Master);

        var html = await PostEventoAsync(client, new Dictionary<string, string>
        {
            ["Input.Title"] = "Festival Recreio",
            ["Input.QuizType"] = nameof(QuizType.Ambiental),
            ["Input.EndsAt"] = "",
        });

        Assert.Contains("href=\"#Input_EndsAt\"", html);
        Assert.Contains("aria-describedby=\"Input_EndsAt-ajuda Input_EndsAt-erro\"", html);
        Assert.Contains("id=\"Input_EndsAt-erro\" class=\"field-error\"", html);
        Assert.DoesNotContain("Input_Title-erro", html);
        Assert.Contains("<fieldset class=\"field full\">", html);
    }
}
