using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;
using Helinho.Web.Panel;

namespace Helinho.IntegrationTests;

/// <summary>Casca do tema Recreio (Issue #140): menu em grupos, item atual, gaveta, logos, tema e favicon.</summary>
[Collection(PostgresCollection.Name)]
public partial class PanelCascaTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<img\\b[^>]*>")]
    private static partial Regex ImgTag();

    [GeneratedRegex("<script\\b[^>]*>")]
    private static partial Regex ScriptTag();

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
    public async Task Casca_liga_o_painel_js_com_defer_e_aplica_o_tema_guardado_antes_do_css()
    {
        var html = await GetAsync(RoleNames.Master, "/Dashboard");

        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var tema = Assert.Single(ScriptTag().Matches(head), m => m.Value.Contains("src=\"/js/tema.js", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("defer", tema);
        Assert.DoesNotContain("async", tema);
        Assert.True(head.IndexOf("/js/tema.js", StringComparison.Ordinal) < head.IndexOf("/css/theme.css", StringComparison.Ordinal));

        var painel = Assert.Single(ScriptTag().Matches(html), m => m.Value.Contains("src=\"/js/painel.js", StringComparison.Ordinal)).Value;
        Assert.Matches("""\sdefer[\s>=]""", painel);
    }

    [Fact]
    public async Task Scripts_da_casca_respondem_200_e_usam_a_mesma_chave_de_tema()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();

        var tema = await client.GetStringAsync("/js/tema.js", Ct);
        var painel = await client.GetStringAsync("/js/painel.js", Ct);

        // tema.js só aplica o que o botão do painel.js guarda: mesma chave e mesmo atributo.
        Assert.Contains("localStorage.getItem('tema')", tema);
        Assert.Contains("localStorage.setItem('tema'", painel);
        Assert.Contains("dataset.theme", tema);
        Assert.Contains("[data-tema]", painel);
    }

    [Fact]
    public async Task Botao_de_abrir_e_bloco_do_usuario_mantem_os_contratos()
    {
        var html = await GetAsync(RoleNames.Master, "/Dashboard");

        Assert.Matches("""<button class="menu-toggle"[^>]*data-bs-target="#menu"[^>]*aria-controls="menu"[^>]*>""", html);
        // Sem .menu-btn/.scrim: o módulo de gaveta do painel.js sai cedo e não disputa com o offcanvas.
        Assert.DoesNotMatch("""class="[^"]*\b(menu-btn|scrim)\b""", html);
        Assert.Contains("class=\"me user-block\"", html);
        Assert.Contains("<b>Pessoa de Teste</b>", html);
        Assert.Contains("<span>Master</span><span>Todas as unidades</span>", html);
        Assert.Contains(">PT</span>", html);
        Assert.Equal(NavMenu.For(_ => true).Count, Regex.Count(html, "<span class=\"tile\""));
    }

    [Fact]
    public async Task Todo_item_do_menu_tem_cor_de_ladrilho_e_icone_no_sprite()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateClientAs(RoleNames.Master);
        var css = await client.GetStringAsync("/css/theme.css", Ct);
        var html = await client.GetStringAsync("/Dashboard", Ct);

        foreach (var item in NavMenu.For(_ => true))
        {
            Assert.Contains($"--tile-{item.Tile}:", css);
            Assert.Contains($"<symbol id=\"{item.Icon}\"", html);
        }
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
