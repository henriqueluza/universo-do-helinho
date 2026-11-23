using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286, fatia 2: pedir rascunhos de IA em /questoes/ia (ADR-0004 §15). Só o gerador Fake.</summary>
[Collection(PostgresCollection.Name)]
public partial class AiTelaPedirTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();

    private async Task<HelinhoWebFactory> NewFactoryAsync(string provider = AiOptions.Fake, int quota = 60)
    {
        var factory = new HelinhoWebFactory(postgres, configure: services =>
            services.Configure<AiOptions>(o => { o.Provider = provider; o.DailyQuotaPerAdmin = quota; }));
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, params (string Key, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync("/Questoes/Ia", Ct)).Groups[1].Value;
        var form = new List<KeyValuePair<string, string>>(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)))
        {
            new("__RequestVerificationToken", token),
        };
        return await client.PostAsync("/Questoes/Ia", new FormUrlEncodedContent(form), Ct);
    }

    private static (string, string)[] Valid(string subject = "", string count = "5") =>
        [("Input.QuizType", "Esportivo"), ("Input.AgeBand", "Age8To10"), ("Input.Count", count), ("Input.Subject", subject)];

    [Fact]
    public async Task Com_a_IA_desligada_a_tela_responde_404_e_o_menu_nao_a_anuncia()
    {
        await using var factory = await NewFactoryAsync(AiOptions.Off);
        using var client = factory.CreateFormClientAs(RoleNames.Master);

        using var index = await client.GetAsync("/Questoes/Ia", Ct);
        using var pedido = await client.GetAsync($"/Questoes/Ia/{Guid.NewGuid()}", Ct);
        using var post = await client.PostAsync("/Questoes/Ia", new FormUrlEncodedContent([]), Ct);
        var menu = await client.GetStringAsync("/Questoes", Ct);

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pedido.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
        Assert.DoesNotContain("Rascunhos de IA", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("/Questoes/Ia", menu, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formulario_tem_os_campos_o_aviso_de_demonstracao_o_estado_vazio_e_o_item_de_menu_ativo()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes/Ia", Ct));

        Assert.Contains("Modo de demonstração: sugestões fictícias.", html, StringComparison.Ordinal);
        Assert.Contains("<form class=\"ai-panel\" method=\"post\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        foreach (var id in new[] { "Input_QuizType", "Input_AgeBand", "Input_Count", "Input_Subject" })
        {
            Assert.Matches($"<label for=\"{id}\"", html);
        }

        Assert.Equal(3, Regex.Count(html, "<option value=\"\\d+\"")); // 5, 10 e 20
        Assert.Contains("Nenhum pedido ainda.", html, StringComparison.Ordinal);
        Assert.Contains("Rascunhos de IA", html, StringComparison.Ordinal);
        // Só o item mais específico fica ativo: "Questões" não acende junto.
        Assert.Equal(1, Regex.Count(html, "aria-current=\"page\""));
        Assert.Matches("href=\"/Questoes/Ia\" aria-current=\"page\"", html);
        Assert.DoesNotMatch("<script(?![^>]*\\bsrc=)", html);
        Assert.DoesNotMatch(" style=\"(?!--t:)", html); // só a variável do ladrilho do menu
        Assert.DoesNotContain("onchange", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pedir_responde_303_para_o_pedido_e_o_clique_duplo_leva_ao_mesmo()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);

        using var first = await PostAsync(client, Valid("Esportes coletivos"));
        using var second = await PostAsync(client, Valid("Esportes coletivos"));

        Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
        Assert.Matches("^/Questoes/Ia/[0-9a-f-]{36}$", first.Headers.Location!.OriginalString);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(first.Headers.Location, Ct));
        Assert.Contains("Na fila para gerar", html, StringComparison.Ordinal);
        Assert.Contains("<meta http-equiv=\"refresh\" content=\"5\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains(">Atualizar</a>", html, StringComparison.Ordinal);
        Assert.Contains("assunto: Esportes coletivos", html, StringComparison.Ordinal);

        var jobs = await Recent(factory);
        Assert.Single(jobs);
    }

    private static async Task<List<AiGenerationJob>> Recent(HelinhoWebFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiGenerationJobs.AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public async Task Entrada_invalida_mostra_o_resumo_de_erros_e_mantem_o_que_foi_digitado()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateFormClientAs(RoleNames.Admin);

        using var response = await PostAsync(client, ("Input.QuizType", ""), ("Input.AgeBand", "Age8To10"), ("Input.Count", "7"), ("Input.Subject", "reciclagem"));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"resumo-erros\"", html, StringComparison.Ordinal);
        Assert.Contains("Escolha o tipo de quiz.", html, StringComparison.Ordinal);
        Assert.Contains("Escolha 5, 10 ou 20 questões.", html, StringComparison.Ordinal);
        Assert.Contains("value=\"reciclagem\"", html, StringComparison.Ordinal);
        Assert.Matches("<option value=\"Age8To10\" selected", html);
        Assert.Empty(await Recent(factory));
    }

    [Fact]
    public async Task Cota_esgotada_diz_o_limite_e_quando_o_saldo_volta()
    {
        await using var factory = await NewFactoryAsync(quota: 5);
        using var client = factory.CreateFormClientAs(RoleNames.Admin);

        using var first = await PostAsync(client, Valid("um"));
        using var second = await PostAsync(client, Valid("outro"));
        var html = WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.SeeOther, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Limite de pedidos atingido", html, StringComparison.Ordinal);
        Assert.Contains("Você chegou ao seu limite diário (5 sugestões).", html, StringComparison.Ordinal);
        Assert.Matches("O saldo volta às \\d\\d:\\d\\d de \\d\\d/\\d\\d\\.", html);
        Assert.Single(await Recent(factory));
    }

    [Fact]
    public void Cota_global_e_dita_como_da_equipe()
    {
        var quota = new AiQuotaExceeded(true, 300, 2, DateTimeOffset.UtcNow.AddHours(3));
        var text = Helinho.Web.Pages.Questoes.Ia.IaTexto.Quota(quota, 5);
        Assert.Contains("toda a equipe (300 sugestões)", text, StringComparison.Ordinal);
        Assert.Contains("Ainda dá para pedir 2, e você pediu 5.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pedir_de_novo_volta_com_os_mesmos_dados_do_pedido()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateClientAs(RoleNames.Admin);

        var again = WebUtility.HtmlDecode(await client.GetStringAsync("/Questoes/Ia?tipo=Esportivo&faixa=Age8To10&qtd=10&assunto=Esportes%20coletivos", Ct));
        Assert.Matches("<option value=\"Esportivo\" selected", again);
        Assert.Matches("<option value=\"10\" selected", again);
        Assert.Contains("value=\"Esportes coletivos\"", again, StringComparison.Ordinal);
    }
}
