using System.Globalization;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

/// <summary>
/// Acabamento do Recreio 2/3 (#190), itens 2 e 12, conferidos pela cascata e não pelo texto exato do CSS:
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

        // No escuro, a ponte só troca variáveis do Bootstrap; nenhuma regra que vence a camada components
        // (ou a ponte dentro dela) volta a pintar fundo ou borda inferior no cabeçalho ou nas células.
        var camadasQueVencem = new HashSet<string?> { "components", "screens", "motion", null };
        var reintroduzem = regras
            .Where(r => camadasQueVencem.Contains(r.Camada))
            .Where(r => ClasseTable().IsMatch(r.Seletor) && CelulaOuCabecalho().IsMatch(r.Seletor))
            .Where(r => r != cabecalho && r != celulas)
            .Where(r => r.Declaracoes.Keys.Any(p => p.StartsWith("background", StringComparison.Ordinal) || p.StartsWith("border-bottom", StringComparison.Ordinal)))
            .Select(r => $"{r.Seletor} (camada {r.Camada ?? "nenhuma"})")
            .ToList();
        Assert.True(reintroduzem.Count == 0, "regra volta a pintar o cabeçalho ou as células da tabela: " + string.Join("; ", reintroduzem));

        var pontesEscuras = regras.Where(r => r.Seletor.Contains("data-theme=\"dark\"", StringComparison.Ordinal) && ClasseTable().IsMatch(r.Seletor)).ToList();
        Assert.NotEmpty(pontesEscuras);
        Assert.All(pontesEscuras, r => Assert.All(r.Declaracoes.Keys, p => Assert.StartsWith("--", p)));
    }

    [Fact]
    public void Barra_do_criador_ocupa_menos_altura_no_celular()
    {
        var root = FindRepositoryRoot();
        var editar = File.ReadAllText(Path.Combine(root, "src", "Helinho.Web", "Pages", "Questoes", "Editar.cshtml"));
        Assert.Matches(@"<div class=""maker-bar"">\s*<button class=""btn [^""]*""[^>]*>[^<]*</button>\s*<a class=""link""[^>]*>[^<]*</a>\s*<span class=""hint"">", editar);

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

        // A 375 px a dica cai para a segunda linha: altura fixa = padding vertical × 2 + botão + espaço entre linhas
        // (com os tokens de hoje, 84 px na regra geral e 60 px no celular; no Chromium a barra mede 108 → 82 px).
        double Altura(Regra b, Regra btn) =>
            (2 * Px(Primeiro(b.Declaracoes["padding"]), tokens)) + Px(btn.Declaracoes["height"], tokens) + Px(Primeiro(b.Declaracoes["gap"]), tokens);

        var antes = Altura(barra, botao);
        var depois = Altura(barraCelular, botaoCelular);
        Assert.True(depois < antes, $"no celular a barra deveria encolher: {depois}px contra {antes}px");

        Assert.Equal("0", dicaCelular.Declaracoes["margin-left"]);
        Assert.Equal("100%", dicaCelular.Declaracoes["width"]);
        Assert.True(Rem(dicaCelular.Declaracoes["font-size"], tokens) < Rem(dica.Declaracoes["font-size"], tokens), "a dica no celular usa fonte menor");
    }

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
