using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>Telas de conta no tema Recreio (Issue #151): Entrar, Recuperar senha e Acesso negado.</summary>
[Collection(PostgresCollection.Name)]
public partial class ContaRecreioTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex Img();

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    private static void AssertImagensComDimensoes(string html)
    {
        foreach (Match img in Img().Matches(html))
        {
            Assert.Contains("width=\"", img.Value);
            Assert.Contains("height=\"", img.Value);
            Assert.Contains("alt=\"", img.Value);
        }
    }

    [Fact]
    public async Task Entrar_tem_cena_cartao_logos_personagens_e_favicon()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var html = await client.GetStringAsync("/entrar", Ct);

        Assert.Contains("class=\"auth\"", html);
        Assert.Contains("class=\"auth-scene\"", html);
        Assert.Contains("class=\"auth-form\"", html);
        Assert.Contains("class=\"auth-card\"", html);
        foreach (var img in new[] { "logo-jogo", "fundhas-invertido", "cara-lia", "cara-byte", "cara-luna", "cara-flora", "cara-max", "cara-helinho" })
        {
            Assert.Contains($"/img/{img}.webp", html);
        }

        Assert.Contains("rel=\"icon\" href=\"/img/helinho-rosto.webp\"", html);
        Assert.Contains("autocomplete=\"username\"", html);
        Assert.Contains("autocomplete=\"current-password\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Equal(1, Regex.Count(html, "<h1\\b"));
        AssertImagensComDimensoes(html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<noscript", html);
        Assert.DoesNotContain("onchange", html);
        Assert.DoesNotContain("--ai", html);
        Assert.DoesNotContain("btn-ai", html);
    }

    [Fact]
    public async Task Entrar_com_campos_vazios_mostra_resumo_acessivel_e_alerta_do_tema()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();
        var page = await client.GetStringAsync("/entrar", Ct);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.UserName"] = "",
            ["Input.Password"] = "",
        }), Ct);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct));

        Assert.Contains("class=\"alert\" role=\"alert\" tabindex=\"-1\" id=\"resumo-erros\"", html);
        Assert.Contains("class=\"field-error\"", html);
        Assert.Contains("Informe o usuário ou e-mail.", html);
        Assert.Contains("aria-describedby=\"Input_Password-erro senha-dica\"", html);
    }

    [Fact]
    public async Task Recuperar_senha_e_acesso_negado_usam_o_cartao_bare_com_imagens_dimensionadas()
    {
        await using var factory = await NewFactoryAsync();
        using var anon = factory.CreateAnonymousClient();
        using var admin = factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var recuperar = await anon.GetStringAsync("/recuperar-senha", Ct);
        var negado = await admin.GetAsync("/acesso-negado", Ct);
        var negadoHtml = await negado.Content.ReadAsStringAsync(Ct);

        Assert.Contains("class=\"bare\"", recuperar);
        Assert.Contains("bare-card", recuperar);
        Assert.Contains("rel=\"icon\" href=\"/img/helinho-rosto.webp\"", recuperar);
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        Assert.Contains("bare-card", negadoHtml);
        Assert.Contains("href=\"/\"", negadoHtml);
        Assert.Contains("action=\"/sair\"", negadoHtml);
        AssertImagensComDimensoes(recuperar);
        AssertImagensComDimensoes(negadoHtml);
    }
}
