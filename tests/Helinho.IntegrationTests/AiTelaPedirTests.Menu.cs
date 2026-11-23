using System.Net;
using System.Text.RegularExpressions;
using Helinho.Application.Ai;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>Issue #286: a tela de pedir com a IA desligada e o item de menu (ADR-0004 §14 e §15). Arquivo da segunda entrega (Issue #303).</summary>
public partial class AiTelaPedirTests
{
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
}
