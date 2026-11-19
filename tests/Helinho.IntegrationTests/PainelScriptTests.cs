using System.Net;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

[Collection(PostgresCollection.Name)]
public partial class PainelScriptTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpResponseMessage Response, string Body)> GetScriptAsync()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();
        var response = await client.GetAsync("/js/painel.js", Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    // O arquivo não muda durante a execução: os testes de conteúdo baixam uma vez só, em vez de subir
    // o host e o banco a cada caso. Os testes da coleção rodam em sequência, sem corrida no cache.
    private static string? corpoEmCache;

    private async Task<string> GetBodyAsync() => corpoEmCache ??= (await GetScriptAsync()).Body;

    [Fact]
    public async Task Painel_js_e_servido_sem_login_como_javascript()
    {
        var (response, body) = await GetScriptAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tipo = response.Content.Headers.ContentType?.MediaType;
        Assert.True(tipo is "text/javascript" or "application/javascript", $"Tipo inesperado: {tipo}");
        Assert.StartsWith("'use strict';", body);
    }

    // Ficam: cada módulo da preview continua no arquivo servido. Apagar um módulo faz este teste falhar.
    [Theory]
    [InlineData("[data-tema]")] // tema claro/escuro
    [InlineData("startViewTransition")]
    [InlineData("[data-count]")] // contagem dos números
    [InlineData(".menu-btn")] // gaveta do menu
    [InlineData(".scrim")]
    [InlineData(".rail-close")]
    [InlineData("new IntersectionObserver")] // entrada das seções
    [InlineData("classList.add('reveal')")]
    [InlineData(".linechart[data-fonte]")] // gráfico de linha
    [InlineData("svg.spark[data-spark]")] // minigráfico
    [InlineData("[data-tip]")] // dicas
    [InlineData("tip.replaceChildren(")]
    [InlineData("createElementNS")]
    [InlineData("matchMedia('(prefers-reduced-motion: reduce)')")]
    public async Task Painel_js_mantem_os_modulos_da_preview(string marcador)
    {
        var body = await GetBodyAsync();

        Assert.Contains(marcador, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dica_e_montada_por_textContent()
    {
        var body = await GetBodyAsync();

        Assert.Matches(AtribuiTextContent(), body);
    }

    [Fact]
    public async Task Tema_persiste_no_localStorage_sempre_dentro_de_try()
    {
        var body = await GetBodyAsync();

        Assert.Contains("localStorage.getItem('tema')", body, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem('tema'", body, StringComparison.Ordinal);
        Assert.Equal(AcessoAoLocalStorage().Count(body), AcessoAoLocalStorageEmTry().Count(body));
    }

    // Inofensivo sem o elemento: o módulo sai cedo quando o que ele procura não existe.
    [Theory]
    [InlineData("[data-count]", "alvos")]
    [InlineData(".menu-btn", "btn")]
    [InlineData(".auth-scene", "cena")]
    [InlineData("new IntersectionObserver", "main")]
    [InlineData(".linechart[data-fonte]", "graficos")]
    [InlineData(".linechart[data-fonte]", "tab")]
    public async Task Modulo_sai_cedo_quando_o_elemento_nao_existe(string marcador, string variavel)
    {
        var body = await GetBodyAsync();

        Assert.Matches($@"if \(!{Regex.Escape(variavel)}\b[^;{{]*\)\s*return;", Modulo(body, marcador));
    }

    // O tema não depende do botão: sem [data-tema] ele só aplica a preferência salva.
    [Fact]
    public async Task Modulo_do_tema_nao_exige_o_botao()
    {
        var body = await GetBodyAsync();
        var tema = Modulo(body, "startViewTransition");

        Assert.Contains("querySelectorAll('[data-tema]')", tema, StringComparison.Ordinal);
        Assert.DoesNotContain("querySelector(", tema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("startViewTransition")] // tema: troca sem revelação circular
    [InlineData("[data-count]")] // números: ficam no valor final do HTML
    [InlineData(".auth-scene")] // cena do login parada
    [InlineData("new IntersectionObserver")] // seções sem efeito de entrada
    public async Task Modulo_com_movimento_respeita_prefers_reduced_motion(string marcador)
    {
        var body = await GetBodyAsync();

        Assert.Matches(CalmoLeReducaoDeMovimento(), body);
        Assert.Contains("calmo()", Modulo(body, marcador), StringComparison.Ordinal);
    }

    // Sem JS tudo legível: o script não oculta nada por conta própria. A única ocultação (.reveal sob .js)
    // nasce no módulo de entrada, que a desfaz na rolagem, na impressão e ao ligar "reduzir movimento".
    [Fact]
    public async Task Painel_js_so_oculta_o_que_ele_mesmo_revela()
    {
        var body = await GetBodyAsync();
        var entrada = Modulo(body, "new IntersectionObserver");

        Assert.DoesNotMatch(Ocultacao(), body);
        Assert.Equal(1, AdicionaClasseJs().Count(body));
        Assert.Matches(AdicionaClasseJs(), entrada);
        Assert.Contains("classList.add('vis')", entrada, StringComparison.Ordinal);
        Assert.Contains("addEventListener('beforeprint'", entrada, StringComparison.Ordinal);
        Assert.Contains("reduz.addEventListener('change'", entrada, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("estado")] // estados de demonstração (?estado=...)
    [InlineData("URLSearchParams")]
    [InlineData("Maria Fernandes")] // feed "ao vivo" de exemplo
    [InlineData("feed")]
    [InlineData("Tudo certo")] // envio simulado do login
    [InlineData("Entrando")]
    [InlineData("resumo-erros")]
    public async Task Painel_js_nao_contem_codigo_so_da_preview(string trecho)
    {
        var body = await GetBodyAsync();

        Assert.DoesNotContain(trecho, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("setTimeout(\"")]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("sendBeacon")]
    [InlineData("cssText")]
    [InlineData("setAttribute('style'")]
    public async Task Painel_js_nao_usa_apis_proibidas(string api)
    {
        var body = await GetBodyAsync();

        Assert.DoesNotContain(api, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Painel_js_nao_usa_manipulador_inline_nem_armazenamento_alem_do_tema()
    {
        var body = await GetBodyAsync();

        Assert.DoesNotMatch(ManipuladorInline(), body);
        Assert.DoesNotContain("sessionStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("document.cookie", body, StringComparison.Ordinal);
        Assert.All(ChavesDeLocalStorage().Matches(body), m => Assert.Equal("'tema'", m.Groups[1].Value));
    }

    // O detector de manipulador inline pega qualquer evento, não só os mais comuns.
    [Theory]
    [InlineData("el.onfocus = f;")]
    [InlineData("el.onmouseover=f;")]
    [InlineData("el.onkeydown = () => {};")]
    [InlineData("el.onclick = f;")]
    [InlineData("el.setAttribute('onload', 'x()');")]
    [InlineData("el.setAttribute(\"onpointerenter\", s);")]
    [InlineData("el['onblur'] = f;")]
    public void Detector_de_manipulador_inline_pega_qualquer_evento(string trecho) =>
        Assert.Matches(ManipuladorInline(), trecho);

    [Theory]
    [InlineData("el.addEventListener('focus', f);")]
    [InlineData("tip.classList.add('on');")]
    [InlineData("if (navigator.online === true) f();")]
    public void Detector_de_manipulador_inline_ignora_codigo_permitido(string trecho) =>
        Assert.DoesNotMatch(ManipuladorInline(), trecho);

    // Cada módulo é um bloco "(() => {" no início da linha; devolve o único bloco que contém o marcador.
    private static string Modulo(string body, string marcador) =>
        Assert.Single(InicioDeModulo().Split(body), m => m.Contains(marcador, StringComparison.Ordinal));

    [GeneratedRegex(@"setAttribute\(\s*['""]on\w+|\.on[a-zA-Z]+\s*=(?!=)|\[\s*['""]on\w+['""]\s*\]\s*=(?!=)")]
    private static partial Regex ManipuladorInline();

    [GeneratedRegex(@"^\(\(\) => \{\r?$", RegexOptions.Multiline)]
    private static partial Regex InicioDeModulo();

    [GeneratedRegex(@"\.textContent\s*=\s*\w")]
    private static partial Regex AtribuiTextContent();

    [GeneratedRegex(@"localStorage\.\w+")]
    private static partial Regex AcessoAoLocalStorage();

    [GeneratedRegex(@"try\s*\{[^{}]*localStorage\.\w+")]
    private static partial Regex AcessoAoLocalStorageEmTry();

    [GeneratedRegex(@"const reduz = matchMedia\('\(prefers-reduced-motion: reduce\)'\);\s*const calmo = \(\) => reduz\.matches;")]
    private static partial Regex CalmoLeReducaoDeMovimento();

    [GeneratedRegex(@"\.hidden\s*=(?!=)(?!\s*false\b)|setAttribute\(\s*['""]hidden|\.style\.(display|visibility|opacity)\b|classList\.(add|toggle)\(\s*['""](hidden|d-none|invisible|visually-hidden)")]
    private static partial Regex Ocultacao();

    [GeneratedRegex(@"classList\.add\('js'\)")]
    private static partial Regex AdicionaClasseJs();

    [GeneratedRegex(@"localStorage\.\w+Item\(\s*([^,)]+)")]
    private static partial Regex ChavesDeLocalStorage();
}
