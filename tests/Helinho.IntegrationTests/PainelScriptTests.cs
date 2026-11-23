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
    [InlineData("[data-maker]")] // prévia ao vivo do criador de questões (#160)
    [InlineData("[data-bind]")]
    [InlineData("[data-check]")]
    [InlineData("[data-count-for]")]
    [InlineData("[data-ready-hint]")]
    [InlineData("[data-limpar-endereco]")] // definir senha (#240): tira o token da barra de endereço
    [InlineData("history.replaceState(null, '', location.pathname)")]
    [InlineData("/[?&]token=/i.test(location.search)")] // só mexe no endereço que traz o token
    [InlineData("[data-sugerir]")] // "Sugerir com IA": retorno enquanto a página recarrega (#288)
    [InlineData("[data-sugerir-aviso]")]
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
    [InlineData("[data-maker]", "maker")]
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

    // Issue #196: o foco por teclado numa célula fora da tela rola a página; essa rolagem não pode apagar
    // a dica. O comportamento em si só se vê no navegador (roteiro em docs/handoff/196.md); aqui o teste
    // trava a forma do código: o scroll não é mais ligado direto a esconder(), reposiciona a dica do alvo
    // que ainda tem foco, e roda, toque e tecla do usuário encerram essa exceção.
    [Fact]
    public async Task Rolagem_causada_pelo_foco_reposiciona_a_dica_em_vez_de_escondela()
    {
        var body = await GetBodyAsync();
        var dicas = Modulo(body, "tip.replaceChildren(");

        Assert.DoesNotContain("addEventListener('scroll', esconder", dicas, StringComparison.Ordinal);
        var rolagem = Assert.Single(RolagemDaDica().Matches(dicas)).Value;
        Assert.Contains("document.activeElement === alvoFoco.alvo", rolagem, StringComparison.Ordinal);
        Assert.Contains("alvoFoco.on()", rolagem, StringComparison.Ordinal);
        Assert.Contains("else esconder()", rolagem, StringComparison.Ordinal);
        Assert.Contains("['wheel', 'touchmove', 'keydown']", dicas, StringComparison.Ordinal);
        Assert.Contains("addEventListener('blur', () => {", dicas, StringComparison.Ordinal);
        Assert.Contains("e.key === 'Escape') { esconder();", dicas, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"addEventListener\('scroll', \(\) => \{.*?\}, \{ passive: true \}\);", RegexOptions.Singleline)]
    private static partial Regex RolagemDaDica();

    [Fact]
    public async Task Painel_js_nao_usa_manipulador_inline_nem_armazenamento_alem_do_tema()
    {
        var body = await GetBodyAsync();

        Assert.DoesNotMatch(ManipuladorInline(), body);
        Assert.DoesNotContain("sessionStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("document.cookie", body, StringComparison.Ordinal);
        Assert.All(ChavesDeLocalStorage().Matches(body), m => Assert.Equal("'tema'", m.Groups[1].Value));
    }

    // Issue #160: prévia, checklist e contador do criador de questões acompanham o formulário. O módulo
    // só escreve texto por textContent e monta nós por createElement; sem o formulário, não faz nada.
    [Fact]
    public async Task Criador_escreve_por_textContent_e_monta_nos_por_createElement()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Matches(AtribuiTextContent(), maker);
        Assert.Contains("document.createElement('img')", maker, StringComparison.Ordinal);
        Assert.Contains("document.createElement('span')", maker, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", maker, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", maker, StringComparison.Ordinal);
        Assert.Contains("querySelectorAll('[data-bind]')", maker, StringComparison.Ordinal);
        Assert.Contains("[data-show=\"${k}\"]", maker, StringComparison.Ordinal);
    }

    // Issue #160: os ganchos que o servidor emite em Editar.cshtml são os que o módulo procura.
    [Theory]
    [InlineData("[data-preview]")]
    [InlineData(".theme-pick input[data-k]")]
    [InlineData("[data-group=\"age\"] input:checked")]
    [InlineData("input[name=\"Input.Correct\"]:checked")]
    [InlineData("[data-alt]")]
    [InlineData("[data-tick]")]
    [InlineData("[data-face]")]
    [InlineData("[data-check-text]")]
    public async Task Criador_procura_os_ganchos_emitidos_pelo_servidor(string gancho)
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Contains(gancho, maker, StringComparison.Ordinal);
    }

    private static readonly string[] ChavesDoCriador =
    [
        "tema:", "age:", "q:", "alts:", "right:", "['a0', 'a1', 'a2', 'a3']", "show('tname'", "show('age'",
    ];

    // Issue #160: as chaves do checklist e da prévia são as mesmas que QuestoesRecreioTests confere no HTML
    // (data-check e data-bind); renomear um lado quebra um teste. Nenhuma regra de tamanho mínimo da preview.
    [Fact]
    public async Task Criador_checklist_usa_as_chaves_do_servidor_e_os_textos_feito_e_falta()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Contains("' (feito)'", maker, StringComparison.Ordinal);
        Assert.Contains("' (falta)'", maker, StringComparison.Ordinal);
        Assert.All(ChavesDoCriador, chave => Assert.Contains(chave, maker, StringComparison.Ordinal));
        Assert.DoesNotContain(">= 10", maker, StringComparison.Ordinal);
    }

    // Issue #160: a dica viva só é gravada quando muda (não repete o anúncio do leitor de tela) e o
    // aria-live liga só depois da primeira atualização, para a carga da página não ser anunciada.
    [Fact]
    public async Task Criador_dica_e_gravada_so_quando_muda_e_aria_live_liga_depois_da_primeira_atualizacao()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Contains("[data-ready-hint]", maker, StringComparison.Ordinal);
        Assert.Contains("'Tudo pronto. '", maker, StringComparison.Ordinal);
        Assert.Contains("'Falta 1 item. '", maker, StringComparison.Ordinal);
        Assert.Contains("`Faltam ${faltam} itens. `", maker, StringComparison.Ordinal);
        Assert.Matches(@"dica\.textContent\s*!==", maker);
        var primeira = maker.LastIndexOf("atualizar();", StringComparison.Ordinal);
        var live = maker.IndexOf("dica?.setAttribute('aria-live', 'polite')", StringComparison.Ordinal);
        Assert.True(primeira >= 0, "Falta a chamada atualizar();");
        Assert.True(live > primeira, "aria-live deve ser ligado depois da primeira atualizar();");
    }

    // Issue #160: o contador nasce oculto (hidden no HTML) e só o módulo o revela.
    [Fact]
    public async Task Criador_contador_so_e_revelado_pelo_modulo_com_hidden_false()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Contains("[data-count-for]", maker, StringComparison.Ordinal);
        Assert.Matches(@"\.hidden\s*=\s*false\b", maker);
        Assert.Contains("${comoEnviado(campo.value).length}/${campo.maxLength}", maker, StringComparison.Ordinal);
        Assert.DoesNotMatch(Ocultacao(), maker);
    }

    // Issue #160: prévia e contador contam a quebra de linha como o servidor (\r\n), senão o contador
    // diria 500 e o servidor recusaria.
    [Fact]
    public async Task Criador_conta_a_quebra_de_linha_como_o_servidor()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Matches(@"replace\(/\\r\?\\n/g,\s*'\\r\\n'\)", maker);
        Assert.Contains("comoEnviado(campo.value).length", maker, StringComparison.Ordinal);
    }

    // Issue #160: o ✓ só muda de lugar quando a alternativa certa muda.
    [Fact]
    public async Task Criador_move_o_tick_so_quando_a_certa_muda()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Matches(@"tick\.parentElement\s*!==\s*alvo", maker);
    }

    // Issue #160: sem animação por JS; o movimento da prévia é CSS e já respeita reduced-motion.
    [Theory]
    [InlineData("animate(")]
    [InlineData("requestAnimationFrame")]
    [InlineData("setTimeout")]
    [InlineData("startViewTransition")]
    public async Task Criador_nao_anima_por_JavaScript(string api)
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.DoesNotContain(api, maker, StringComparison.Ordinal);
    }

    // Issue #160: acompanha o formulário a cada digitação e a cada troca de opção.
    [Fact]
    public async Task Criador_ouve_input_e_change_do_formulario()
    {
        var maker = Modulo(await GetBodyAsync(), "[data-maker]");

        Assert.Contains("const maker = document.querySelector('[data-maker]');", maker, StringComparison.Ordinal);
        Assert.Contains("maker.addEventListener('input', atualizar)", maker, StringComparison.Ordinal);
        Assert.Contains("maker.addEventListener('change', atualizar)", maker, StringComparison.Ordinal);
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
