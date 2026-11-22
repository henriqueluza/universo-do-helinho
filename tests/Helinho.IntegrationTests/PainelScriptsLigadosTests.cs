using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>Issue #164: painel.js e tema.js ligados nos layouts, sem script inline.</summary>
[Collection(PostgresCollection.Name)]
public partial class PainelScriptsLigadosTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex("<script\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex("<script\\b(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSemSrc();

    [GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ManipuladorInline();

    private async Task<string> GetAsync(string? role, string path)
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = role is null ? factory.CreateAnonymousClient() : factory.CreateClientAs(role);
        return await client.GetStringAsync(path, Ct);
    }

    [Theory]
    [InlineData(null, "/Entrar")]
    [InlineData(RoleNames.Master, "/Dashboard")]
    [InlineData(RoleNames.Master, "/Alunos")]
    [InlineData(RoleNames.Admin, "/Dashboard")]
    public async Task Pagina_liga_o_tema_bloqueante_antes_do_css_e_o_painel_com_defer(string? role, string path)
    {
        var html = await GetAsync(role, path);

        var tema = ScriptTag().Matches(html).Single(m => m.Value.Contains("/js/tema.js"));
        Assert.DoesNotContain("defer", tema.Value);
        Assert.DoesNotContain("async", tema.Value);
        Assert.True(html.IndexOf("/js/tema.js", StringComparison.Ordinal) < html.IndexOf("/css/theme.css", StringComparison.Ordinal));
        Assert.True(html.IndexOf("/js/tema.js", StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal));

        var painel = ScriptTag().Matches(html).Single(m => m.Value.Contains("/js/painel.js"));
        Assert.Contains("defer", painel.Value);
        Assert.True(html.IndexOf("/js/painel.js", StringComparison.Ordinal) > html.IndexOf("</main>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "/Entrar")]
    [InlineData(RoleNames.Master, "/Dashboard")]
    [InlineData(RoleNames.Master, "/Alunos")]
    [InlineData(RoleNames.Admin, "/Alunos/Novo")]
    [InlineData(RoleNames.Admin, "/Dashboard")]
    public async Task Nenhum_script_inline_nem_manipulador_inline(string? role, string path)
    {
        var html = await GetAsync(role, path);

        Assert.Empty(ScriptSemSrc().Matches(html));
        Assert.Empty(ManipuladorInline().Matches(html));
    }

    [Fact]
    public async Task Tema_js_e_servido_e_so_aceita_light_ou_dark_dentro_de_try()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();
        var response = await client.GetAsync("/js/tema.js", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("localStorage.getItem('tema')", body);
        Assert.Contains("=== 'light' || t === 'dark'", body);
        Assert.Contains("dataset.theme", body);
        Assert.True(body.IndexOf("try", StringComparison.Ordinal) < body.IndexOf("localStorage", StringComparison.Ordinal));
        Assert.DoesNotContain("setItem", body);

        var painel = await client.GetAsync("/js/painel.js", Ct);
        Assert.Equal(HttpStatusCode.OK, painel.StatusCode);
    }

    [Fact]
    public async Task Menu_nao_tem_menu_btn_nem_scrim_para_o_modulo_de_gaveta_do_painel_js()
    {
        var html = await GetAsync(RoleNames.Master, "/Dashboard");

        Assert.DoesNotContain("menu-btn", html);
        Assert.DoesNotContain("class=\"scrim", html);
        Assert.Contains("data-bs-toggle=\"offcanvas\"", html);
    }
}
