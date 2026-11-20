using System.Globalization;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

/// <summary>
/// Acabamento do Recreio 2/3 (#190), itens 1, 2, 10 e 12, conferidos pela cascata e não pelo texto exato do CSS:
/// cada regra é lida com a camada e o <c>@media</c> em que está, as declarações viram pares propriedade/valor
/// e os tamanhos saem dos tokens. A suíte não tem motor de navegador (pacote novo exige Issue e ADR); a medição
/// em Chromium fica no PR.
/// </summary>
public partial class TemaAcabamentoCascataTests
{
    private sealed record Regra(string Seletor, IReadOnlyDictionary<string, string> Declaracoes, string? Camada, IReadOnlyList<string> Medias, int Posicao);

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comentario();

    [GeneratedRegex(@"^@layer\s+([\w-]+)$")]
    private static partial Regex CabecalhoDeCamada();

    [GeneratedRegex(@"\.table(?![\w-])")]
    private static partial Regex ClasseTable();

    [GeneratedRegex(@"(?<![\w.#-])(th|thead|td)(?![\w-])")]
    private static partial Regex CelulaOuCabecalho();

    [GeneratedRegex(@"^var\(--([\w-]+)\)$")]
    private static partial Regex Var();

    [GeneratedRegex(@"^:root(\[data-theme=""\w+""\])?\s*")]
    private static partial Regex PrefixoDeTema();

    [GeneratedRegex(@"<div class=""maker-bar"">(?<bloco>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex BlocoDaBarra();

    /// <summary>Variáveis que o Bootstrap pinta na célula por <c>box-shadow: inset 0 0 0 9999px</c>, que <c>background: none</c> não desfaz.</summary>
    private static readonly string[] PintamPorSombra = ["--bs-table-accent-bg", "--bs-table-bg-type", "--bs-table-bg-state"];

    [Fact]
    public void Cabecalho_e_bordas_da_tabela_sao_os_do_Recreio_nos_dois_temas()
    {
        var css = Css();
        var regras = Regras(css);

        // Bootstrap e legado (que pintam th/td por --bs-table-bg) ficam em camadas abaixo das do Recreio.
        var ordemLegado = css.IndexOf("@layer bootstrap, legado;", StringComparison.Ordinal);
        var ordemRecreio = css.IndexOf("@layer tokens, base, components, screens, motion;", StringComparison.Ordinal);
        Assert.True(ordemLegado >= 0 && ordemRecreio > ordemLegado, "bootstrap e legado precisam ser declarados antes das camadas do Recreio");
        Assert.Contains("bootstrap.min.css\") layer(bootstrap);", css);

        var cabecalho = Unica(regras, ".table thead th", "components");
        Assert.Equal("none", cabecalho.Declaracoes["background"]);
        Assert.Equal("0", cabecalho.Declaracoes["border-bottom"]);

        var celulas = Unica(regras, ".table td, .table tbody th", "components");
        Assert.Equal("0", celulas.Declaracoes["border-bottom"]);
        Assert.Equal("2px solid var(--rule)", celulas.Declaracoes["border-top"]);

        // O cinza do legado (#f3f4f6 no thead) fica na camada legado, abaixo da regra acima.
        var fundosDoLegado = regras.Where(r => r.Declaracoes.TryGetValue("--bs-table-bg", out var v) && !v.StartsWith("var(", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(fundosDoLegado);
        Assert.All(fundosDoLegado, r => Assert.Equal("legado", r.Camada));

        // Nenhuma regra que vence a camada components volta a pintar fundo, borda inferior ou sombra
        // no cabeçalho ou nas células, nem com .table nem com seletor só de elemento (th, thead th).
        var camadasQueVencem = new HashSet<string?> { "components", "screens", "motion", null };
        var reintroduzem = regras
            .Where(r => camadasQueVencem.Contains(r.Camada))
            .Where(r => !ReferenceEquals(r, cabecalho) && !ReferenceEquals(r, celulas))
            .Where(r => MiraCelulaDaTabela(r.Seletor) && r.Declaracoes.Keys.Any(PintaCelula))
            .Select(r => $"{r.Seletor} (camada {r.Camada ?? "nenhuma"})")
            .ToList();
        Assert.True(reintroduzem.Count == 0, "regra volta a pintar o cabeçalho ou as células da tabela: " + string.Join("; ", reintroduzem));

        // No escuro, a ponte só troca variáveis do Bootstrap, e nenhuma delas pinta a célula pela sombra.
        var pontesEscuras = regras.Where(r => r.Seletor.Contains("data-theme=\"dark\"", StringComparison.Ordinal) && ClasseTable().IsMatch(r.Seletor)).ToList();
        Assert.NotEmpty(pontesEscuras);
        Assert.All(pontesEscuras, r => Assert.All(r.Declaracoes.Keys, p =>
        {
            Assert.StartsWith("--", p);
            Assert.DoesNotContain(p, PintamPorSombra);
        }));

        // A borda de 2 px do Recreio (--rule) e o fundo do hover da linha (--canvas) têm valor próprio no escuro.
        string Token(string seletor, string nome) => regras
            .Where(r => r.Camada == "tokens" && r.Seletor == seletor && r.Medias.Count == 0 && r.Declaracoes.ContainsKey(nome))
            .Select(r => r.Declaracoes[nome])
            .First();
        Assert.NotEqual(Token(":root", "--rule"), Token(":root[data-theme=\"dark\"]", "--rule"));
        Assert.NotEqual(Token(":root", "--canvas"), Token(":root[data-theme=\"dark\"]", "--canvas"));
    }

    [Fact]
    public void Barra_do_criador_ocupa_menos_altura_no_celular()
    {
        var root = FindRepositoryRoot();
        var editar = File.ReadAllText(Path.Combine(root, "src", "Helinho.Web", "Pages", "Questoes", "Editar.cshtml"));
        var bloco = BlocoDaBarra().Match(editar);
        Assert.True(bloco.Success, "Questoes/Editar precisa da .maker-bar");
        Assert.Matches(@"<button class=""btn\b", bloco.Groups["bloco"].Value);
        Assert.Matches(@"class=""[^""]*\bhint\b", bloco.Groups["bloco"].Value);

        var regras = Regras(Css());
        var tokens = regras.Where(r => r.Camada == "tokens" && r.Seletor == ":root" && r.Medias.Count == 0)
            .SelectMany(r => r.Declaracoes)
            .GroupBy(d => d.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        var celular = new[] { "(max-width: 767.98px)" };
        var barra = Unica(regras, ".maker-bar", "screens");
        var barraCelular = Unica(regras, ".maker-bar", "screens", celular);
        var botao = regras.Single(r => r.Camada == "components" && r.Medias.Count == 0 && r.Seletor.Split(',').Select(s => s.Trim()).Contains(".maker-bar .btn"));
        var botaoCelular = Unica(regras, ".maker-bar .btn", "screens", celular);
        var dica = Unica(regras, ".maker-bar .hint", "screens");
        var dicaCelular = Unica(regras, ".maker-bar .hint", "screens", celular);

        // Mesma camada e mesma especificidade: a regra do celular só vence se vier depois.
        Assert.True(barraCelular.Posicao > barra.Posicao, ".maker-bar do celular precisa vir depois da regra geral");
        Assert.True(dicaCelular.Posicao > dica.Posicao, ".maker-bar .hint do celular precisa vir depois da regra geral");

        // Cada parte da altura encolhe no celular: padding vertical, botão e espaço entre linhas (a dica vai para a segunda).
        (double Geral, double Celular) padding = (Px(Primeiro(barra.Declaracoes["padding"]), tokens), Px(Primeiro(barraCelular.Declaracoes["padding"]), tokens));
        (double Geral, double Celular) altura = (Px(botao.Declaracoes["height"], tokens), Px(botaoCelular.Declaracoes["height"], tokens));
        (double Geral, double Celular) entreLinhas = (Px(Primeiro(barra.Declaracoes["gap"]), tokens), Px(Primeiro(barraCelular.Declaracoes["gap"]), tokens));
        Assert.True(padding.Celular < padding.Geral, $"padding vertical: {padding.Celular}px no celular contra {padding.Geral}px");
        Assert.True(altura.Celular < altura.Geral, $"botão: {altura.Celular}px no celular contra {altura.Geral}px");
        Assert.True(entreLinhas.Celular < entreLinhas.Geral, $"espaço entre linhas: {entreLinhas.Celular}px no celular contra {entreLinhas.Geral}px");

        // Altura fixa sem a dica, com os tokens de hoje: 84 px na regra geral, 60 px no celular (no Chromium a barra mede 108 → 82 px).
        (double Geral, double Celular) alturaFixaSemDica = ((2 * padding.Geral) + altura.Geral + entreLinhas.Geral, (2 * padding.Celular) + altura.Celular + entreLinhas.Celular);
        Assert.True(alturaFixaSemDica.Celular < alturaFixaSemDica.Geral, $"no celular a barra deveria encolher: {alturaFixaSemDica.Celular}px contra {alturaFixaSemDica.Geral}px");

        Assert.Equal("0", dicaCelular.Declaracoes["margin-left"]);
        Assert.Equal("100%", dicaCelular.Declaracoes["width"]);
        Assert.True(Rem(dicaCelular.Declaracoes["font-size"], tokens) < Rem(dica.Declaracoes["font-size"], tokens), "a dica no celular usa fonte menor");
    }

    [Fact]
    public void Resumo_de_erros_ocupa_a_largura_do_formulario()
    {
        var regras = Regras(Css());

        var aviso = Unica(regras, ".alert", "screens");
        Assert.Equal("100%", aviso.Declaracoes["width"]);

        // Nenhuma regra que vence o Bootstrap volta a limitar o aviso (só o cartão de login libera, com none).
        var camadasDoRecreio = new HashSet<string?> { "tokens", "base", "components", "screens", "motion", null };
        var limites = regras
            .Where(r => camadasDoRecreio.Contains(r.Camada))
            .Where(r => r.Seletor.Split(',').Any(p => Regex.IsMatch(p.Trim(), @"(\.alert|#resumo-erros)(:[\w-]+)?$")))
            .Where(r => r.Declaracoes.TryGetValue("max-width", out var v) && v != "none")
            .Select(r => $"{r.Seletor} {{ max-width: {r.Declaracoes["max-width"]} }}")
            .ToList();
        Assert.True(limites.Count == 0, "o resumo de erros voltou a ter largura máxima: " + string.Join("; ", limites));
    }

    [Fact]
    public void Previa_do_celular_tem_fundo_sem_tema_escolhido()
    {
        var root = FindRepositoryRoot();
        var editar = File.ReadAllText(Path.Combine(root, "src", "Helinho.Web", "Pages", "Questoes", "Editar.cshtml"));
        Assert.Matches(@"<div class=""phone @\(quiz is \{ \} qt \? QuizPresentation\.CssClass\(qt\) : """"\)""", editar); // sem tema, só "phone"

        var regras = Regras(Css());

        // A tela pinta o degradê com --qb sem valor reserva: sem --qb definido, o fundo inteiro vira none.
        var tela = Unica(regras, ".phone .screen", "screens");
        Assert.Contains("var(--qb)", tela.Declaracoes["background"], StringComparison.Ordinal);
        Assert.DoesNotContain("var(--qb,", tela.Declaracoes["background"], StringComparison.Ordinal);

        // O padrão do .phone define --q/--qb/--qi com tokens que existem nos dois temas.
        var padrao = regras.Single(r => r.Seletor == ".phone" && r.Camada == "components" && r.Medias.Count == 0);
        string[] variaveis = ["--q", "--qb", "--qi"];
        Assert.All(variaveis, v => Assert.True(padrao.Declaracoes.ContainsKey(v), $".phone precisa definir {v}"));
        foreach (var token in variaveis.Select(v => Var().Match(padrao.Declaracoes[v])))
        {
            Assert.True(token.Success, "o padrão do .phone deve apontar para um token");
            var nome = "--" + token.Groups[1].Value;
            foreach (var seletor in new[] { ":root", ":root[data-theme=\"dark\"]" })
            {
                Assert.True(
                    regras.Any(r => r.Camada == "tokens" && r.Seletor == seletor && r.Declaracoes.ContainsKey(nome)),
                    $"{nome} precisa existir em {seletor}");
            }
        }

        // O tema escolhido (class="phone amb" etc.) continua vencendo: mesma camada e especificidade, declarado depois.
        foreach (var tema in new[] { ".amb", ".con", ".cul", ".esp", ".div" })
        {
            var regra = Unica(regras, tema, "components");
            Assert.True(regra.Posicao > padrao.Posicao, $"{tema} precisa vir depois do padrão do .phone");
            Assert.True(regra.Declaracoes.ContainsKey("--qb"), $"{tema} precisa definir --qb");
        }
    }

    /// <summary>Alguma parte do seletor mira th/thead/td da tabela: com <c>.table</c> ou só com elementos (<c>th</c>, <c>thead th</c>).</summary>
    private static bool MiraCelulaDaTabela(string seletor) =>
        seletor.Split(',').Select(p => PrefixoDeTema().Replace(p.Trim(), string.Empty)).Any(p =>
            CelulaOuCabecalho().IsMatch(p) && (ClasseTable().IsMatch(p) || p.IndexOfAny(['.', '#', '[']) < 0));

    private static bool PintaCelula(string propriedade) =>
        propriedade is "border" or "border-width" or "border-style" or "border-color" or "border-image"
        || propriedade.StartsWith("background", StringComparison.Ordinal)
        || propriedade.StartsWith("border-bottom", StringComparison.Ordinal)
        || propriedade.StartsWith("border-block", StringComparison.Ordinal)
        || propriedade.StartsWith("box-shadow", StringComparison.Ordinal);

    private static Regra Unica(IEnumerable<Regra> regras, string seletor, string camada, string[]? medias = null)
    {
        medias ??= [];
        return regras.Single(r => r.Seletor == seletor && r.Camada == camada && r.Medias.SequenceEqual(medias));
    }

    private static string Primeiro(string valor) => valor.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private static double Px(string valor, IReadOnlyDictionary<string, string> tokens)
    {
        var resolvido = Resolver(valor, tokens);
        return resolvido.EndsWith("px", StringComparison.Ordinal)
            ? double.Parse(resolvido[..^2], CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"'{valor}' não resolve para px ({resolvido})");
    }

    private static double Rem(string valor, IReadOnlyDictionary<string, string> tokens)
    {
        var resolvido = Resolver(valor, tokens);
        return resolvido.EndsWith("rem", StringComparison.Ordinal)
            ? double.Parse(resolvido[..^3], CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"'{valor}' não resolve para rem ({resolvido})");
    }

    private static string Resolver(string valor, IReadOnlyDictionary<string, string> tokens)
    {
        var atual = valor.Trim();
        for (var i = 0; i < 5 && Var().Match(atual) is { Success: true } m; i++)
        {
            atual = tokens.TryGetValue("--" + m.Groups[1].Value, out var v) ? v.Trim() : throw new InvalidOperationException($"token --{m.Groups[1].Value} não existe");
        }

        return atual;
    }

    /// <summary>Lê as regras do CSS com a camada mais interna e os <c>@media</c> que as envolvem (chaves balanceadas).</summary>
    private static List<Regra> Regras(string css)
    {
        var regras = new List<Regra>();
        var pilha = new Stack<string>();
        var inicio = 0;
        for (var i = 0; i < css.Length; i++)
        {
            switch (css[i])
            {
                case ';':
                    inicio = i + 1; // fim de @layer a, b; ou @import
                    break;
                case '}':
                    pilha.TryPop(out _);
                    inicio = i + 1;
                    break;
                case '{':
                    var cabecalho = Regex.Replace(css[inicio..i], @"\s+", " ").Trim();
                    if (cabecalho.StartsWith('@'))
                    {
                        pilha.Push(cabecalho);
                        inicio = i + 1;
                        break;
                    }

                    var fim = css.IndexOf('}', i);
                    var camada = pilha.Select(h => CabecalhoDeCamada().Match(h)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
                    var medias = pilha.Where(h => h.StartsWith("@media ", StringComparison.Ordinal)).Select(h => h["@media ".Length..]).Reverse().ToList();
                    regras.Add(new Regra(cabecalho, Declaracoes(css[(i + 1)..fim]), camada, medias, i));
                    i = fim;
                    inicio = fim + 1;
                    break;
            }
        }

        return regras;
    }

    private static Dictionary<string, string> Declaracoes(string corpo)
    {
        var declaracoes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parte in corpo.Split(';'))
        {
            var doisPontos = parte.IndexOf(':', StringComparison.Ordinal);
            if (doisPontos > 0)
            {
                declaracoes[parte[..doisPontos].Trim()] = parte[(doisPontos + 1)..].Trim();
            }
        }

        return declaracoes;
    }

    private static string Css() =>
        Comentario().Replace(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Helinho.Web", "wwwroot", "css", "theme.css")), string.Empty);

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Helinho.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Helinho.slnx não encontrado acima do diretório de testes.");
    }
}
