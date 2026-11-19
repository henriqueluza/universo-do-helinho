using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>Casca do tema Recreio (Issue #140): menu em grupos, item atual, gaveta, logos, tema e favicon.</summary>
[Collection(PostgresCollection.Name)]
public partial class PanelCascaTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex ImgTag();

    private async Task<string> GetAsync(string role, string path)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClientAs(role);
        return WebUtility.HtmlDecode(await client.GetStringAsync(path, Ct));
    }

    [Fact]
    public async Task Master_ve_os_grupos_e_so_o_item_atual_marcado()
    {
        var html = await GetAsync(RoleNames.Master, "/Alunos");

        Assert.Contains(">Conteúdo do jogo<", html);
        Assert.Contains(">Gestão<", html);
        Assert.Equal(1, Regex.Count(html, "aria-current=\"page\""));
        Assert.Matches("""<a href="/Alunos" aria-current="page">""", html);
        // Itens só de Master ficam em "Gestão", depois do rótulo.
        Assert.True(html.IndexOf(">Gestão<", StringComparison.Ordinal) < html.IndexOf("href=\"/Unidades\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_nao_ve_o_grupo_Gestao()
    {
        var html = await GetAsync(RoleNames.Admin, "/Dashboard");

        Assert.DoesNotContain(">Gestão<", html);
        Assert.Contains(">Conteúdo do jogo<", html);
    }

    [Fact]
    public async Task Casca_tem_gaveta_pular_para_o_conteudo_botao_de_tema_e_favicon()
    {
        var html = await GetAsync(RoleNames.Master, "/Dashboard");

        Assert.Contains("id=\"menu\"", html);
        Assert.Contains("href=\"#conteudo\"", html);
        Assert.Contains("id=\"conteudo\"", html);
        Assert.Matches("""<button[^>]*\bdata-tema\b[^>]*aria-pressed="false"|<button[^>]*aria-pressed="false"[^>]*\bdata-tema\b""", html);
        Assert.Contains("/img/helinho-rosto.webp", html);
        Assert.DoesNotContain("<noscript", html);
        Assert.DoesNotContain("onchange=", html);
        Assert.DoesNotMatch("""<script(?![^>]*\bsrc=)""", html);
    }

    [Fact]
    public async Task Todo_img_da_casca_tem_largura_altura_e_alt()
    {
        var html = await GetAsync(RoleNames.Master, "/Dashboard");

        var imgs = ImgTag().Matches(html).Select(m => m.Value).ToList();
        Assert.True(imgs.Count >= 4); // logo do menu, logo do topo, FUNDHAS clara e escura
        foreach (var img in imgs)
        {
            Assert.Contains("width=\"", img);
            Assert.Contains("height=\"", img);
            Assert.Contains(" alt=\"", img);
        }
    }

    [Fact]
    public async Task Imagens_da_casca_respondem_200()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();

        foreach (var path in new[] { "/img/logo-jogo.webp", "/img/fundhas.webp", "/img/fundhas-invertido.webp", "/img/helinho-rosto.webp" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, Ct)).StatusCode);
        }
    }
}
