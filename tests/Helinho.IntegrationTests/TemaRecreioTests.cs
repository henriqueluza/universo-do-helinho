using System.Net;
using System.Text.RegularExpressions;
using Helinho.Domain;

namespace Helinho.IntegrationTests;

/// <summary>
/// Fatia 1 do tema Recreio: fontes autohospedadas, ordem das camadas e convivência com o Bootstrap.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class TemaRecreioTests(PostgresFixture postgres)
{
    private static readonly string[] CdnHosts = ["fonts.googleapis.com", "fonts.gstatic.com", "unpkg.com", "cdn.jsdelivr.net", "cdnjs.cloudflare.com"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HelinhoWebFactory> NewFactoryAsync()
    {
        var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        return factory;
    }

    [GeneratedRegex(@"@layer\s+tokens\s*,\s*base\s*,\s*components\s*,\s*screens\s*,\s*motion\s*;")]
    private static partial Regex LayerOrder();

    [GeneratedRegex(@"@import\s+url\(""\.\./lib/bootstrap/bootstrap\.min\.css""\)\s+layer\(bootstrap\)\s*;")]
    private static partial Regex BootstrapImport();

    // O CSS busca recurso por url(...), image-set("...") ou @import "...": nenhum pode apontar para fora (http:, https: ou //).
    [GeneratedRegex(@"(?:url\(\s*|image-set\(\s*|@import\s+)[""']?\s*(?:https?:)?//", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalResource();

    [Fact]
    public async Task Tema_declara_a_ordem_das_camadas_e_isola_o_bootstrap()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        Assert.Matches(LayerOrder(), css);
        Assert.Contains("@layer bootstrap;", css);
        Assert.Matches(BootstrapImport(), css);
        Assert.Contains("@layer tokens {", css);
        Assert.Contains("@layer base {", css);
        // Camadas declaradas antes de qualquer uso; o @import precisa vir antes das regras.
        var import = BootstrapImport().Match(css).Index;
        Assert.True(css.IndexOf("@layer bootstrap;", StringComparison.Ordinal) < import);
        Assert.True(import < css.IndexOf("@layer tokens {", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/lib/bootstrap/bootstrap.min.css", "text/css")]
    [InlineData("/fonts/nunito-latin-wght-normal.woff2", "font/woff2")]
    [InlineData("/fonts/baloo-2-latin-wght-normal.woff2", "font/woff2")]
    public async Task Arquivos_do_tema_respondem_200(string path, string contentType)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Fontes_declaradas_no_tema_existem_e_usam_font_display_swap()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        foreach (var family in new[] { "Nunito", "Baloo 2" })
        {
            var face = Regex.Match(css, $@"@font-face\s*\{{[^}}]*font-family:\s*""{family}""[^}}]*\}}");
            Assert.True(face.Success, $"@font-face de {family} ausente");
            Assert.Contains("font-display: swap", face.Value);
            var file = Regex.Match(face.Value, @"url\(""\.\./fonts/([^""]+\.woff2)""\)").Groups[1].Value;
            var response = await client.GetAsync($"/fonts/{file}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/entrar")]
    [InlineData("/Dashboard")]
    public async Task Paginas_nao_referenciam_cdn_e_nao_carregam_bootstrap_por_link(string path)
    {
        await using var factory = await NewFactoryAsync();
        using var client = path == "/entrar"
            ? factory.CreateAnonymousClient()
            : factory.CreateClientAs(RoleNames.Admin, Guid.NewGuid());

        var html = await client.GetStringAsync(path, Ct);

        Assert.Contains("href=\"/css/theme.css", html);
        Assert.DoesNotContain("bootstrap.min.css", html);
        foreach (var host in CdnHosts)
        {
            Assert.DoesNotContain(host, html);
        }
    }

    [Theory]
    [InlineData(".btn {")]
    [InlineData(".btn-primary {")]
    [InlineData(".btn-lg {")]
    [InlineData(".btn-sm {")]
    [InlineData(".input {")]
    [InlineData(".input[aria-invalid=\"true\"] {")]
    [InlineData(".input[aria-invalid=\"true\"]:focus {")]
    [InlineData(".select {")]
    [InlineData(".field {")]
    [InlineData(".panel {")]
    [InlineData(".tag {")]
    [InlineData(".tag-draft {")]
    [InlineData(".table {")]
    [InlineData(".table-wrap {")]
    [InlineData(".pager {")]
    [InlineData(".form-grid {")]
    [InlineData(".textarea {")]
    [InlineData(".alt {")]
    [InlineData(".note {")]
    [InlineData(".meter {")]
    public async Task Camada_components_tem_os_seletores_chave(string selector)
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        // Só o bloco do Recreio (até a ponte do legado), e o seletor no início da regra:
        // ".input {" não pode passar por causa de ".alt .input {".
        var start = css.IndexOf("@layer components {", StringComparison.Ordinal);
        var end = css.IndexOf("@layer screens {", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Matches(new Regex(@"(?m)^\s*" + Regex.Escape(selector)), css[start..end]);
    }

    [Fact]
    public async Task Tema_nao_tem_a_camada_legado_nem_as_pontes_nem_DM_Sans()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        // Tema antigo (Issue #195): camada, fonte e as regras de ponte que o sustentavam.
        foreach (var removido in new[]
        {
            "@layer legado", "legado,", "DM Sans", "dm-sans", "PONTE",
            ".card-panel", ".section-title", ".auth:not(:has(.auth-scene))", ".auth:has(.auth-scene)",
            ".kpi:not(:has(.spark))", ".text-negative", ".seg-item", ".type-badge",
            "--surface:", "--raised:", "--bs-body-color:", "--bs-card-bg:",
        })
        {
            Assert.DoesNotContain(removido, css);
        }
        // O único resto do legado em uso: .muted (Desempenho/Aluno.cshtml), mantido de propósito.
        Assert.Contains(".muted {", css);

        using var fonte = await client.GetAsync("/fonts/dm-sans-latin-400-normal.woff2", Ct);
        Assert.Equal(HttpStatusCode.NotFound, fonte.StatusCode);
    }

    [Fact]
    public async Task Tema_nao_tem_seletor_de_ia()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        foreach (var forbidden in new[] { ".btn-ai", ".tag-ai", ".note.ai", ".ai-help", ".ai-filled", ".ai-flag", ".ai-panel" })
        {
            Assert.DoesNotContain(forbidden, css);
        }
    }

    [Fact]
    public async Task Css_do_painel_nao_referencia_cdn()
    {
        await using var factory = await NewFactoryAsync();
        using var client = factory.CreateAnonymousClient();

        var css = await client.GetStringAsync("/css/theme.css", Ct);

        foreach (var host in CdnHosts)
        {
            Assert.DoesNotContain(host, css);
        }
        // Endereço em comentário ou no namespace de um SVG em data: URI não carrega nada; só url()/@import importam.
        Assert.Empty(ExternalResource().Matches(css));
    }

    [Theory]
    [InlineData("a { background: url(https://cdn.example/x.png); }")]
    [InlineData("a { background: url( 'http://cdn.example/x.png' ); }")]
    [InlineData("@font-face { src: url(\"//cdn.example/f.woff2\"); }")]
    [InlineData("@import \"https://cdn.example/a.css\";")]
    [InlineData("@import url(https://cdn.example/a.css) layer(x);")]
    [InlineData("a { background: URL(HTTPS://cdn.example/x.png); }")]
    [InlineData("a { background-image: image-set(\"https://cdn.example/x.png\" 1x); }")]
    public void Detector_de_recurso_externo_pega_url_e_import_para_fora(string css) =>
        Assert.NotEmpty(ExternalResource().Matches(css));

    [Theory]
    [InlineData("/* origem: https://fonts.google.com/specimen/Nunito */")]
    [InlineData("a { background: url(\"data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg'/>\"); }")]
    [InlineData("@import url(\"../lib/bootstrap/bootstrap.min.css\") layer(bootstrap);")]
    [InlineData("@font-face { src: url(\"../fonts/nunito-latin-wght-normal.woff2\"); }")]
    public void Detector_de_recurso_externo_ignora_comentario_data_uri_e_caminho_local(string css) =>
        Assert.Empty(ExternalResource().Matches(css));
}
