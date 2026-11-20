using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

/// <summary>CSS das telas internas e de conta do tema Recreio (Issue #144): camadas, seletores-chave, sem IA, movimento reduzido.</summary>
[Collection(PostgresCollection.Name)]
public partial class TemaTelasCssTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"@layer\s+screens\s*\{")]
    private static partial Regex ScreensLayer();

    [GeneratedRegex(@"@layer\s+motion\s*\{")]
    private static partial Regex MotionLayer();

    [GeneratedRegex(@"@media\s*\(prefers-reduced-motion:\s*no-preference\)\s*\{")]
    private static partial Regex NoPreference();

    [GeneratedRegex(@"@keyframes\s+[\w-]+\s*\{")]
    private static partial Regex Keyframes();

    [GeneratedRegex(@"\btransition\s*:[^;}]*")]
    private static partial Regex Transition();

    [GeneratedRegex(@"(\d*\.?\d+)m?s\b")]
    private static partial Regex Duration();

    /// <summary>O CSS servido, sem comentários (um comentário não pode passar por regra).</summary>
    private async Task<string> CssAsync()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();
        return Comment().Replace(await client.GetStringAsync("/css/theme.css", Ct), string.Empty);
    }

    /// <summary>Separa o corpo dos blocos abertos por <paramref name="header"/> (chaves balanceadas) do resto do CSS.</summary>
    private static (string Inside, string Outside) Split(string css, Regex header)
    {
        StringBuilder inside = new(), outside = new();
        var idx = 0;
        foreach (Match m in header.Matches(css))
        {
            if (m.Index < idx)
            {
                continue; // aninhado num bloco já separado
            }

            outside.Append(css, idx, m.Index - idx);
            var depth = 1;
            var i = m.Index + m.Length;
            var start = i;
            while (i < css.Length && depth > 0)
            {
                depth += css[i] == '{' ? 1 : css[i] == '}' ? -1 : 0;
                i++;
            }

            inside.Append(css, start, i - start - 1).Append('\n');
            idx = i;
        }

        outside.Append(css, idx, css.Length - idx);
        return (inside.ToString(), outside.ToString());
    }

    [Fact]
    public async Task Seletores_das_telas_ficam_na_camada_screens()
    {
        var screens = Split(await CssAsync(), ScreensLayer()).Inside;

        foreach (var selector in new[]
        {
            ".stack {", ".filters-bar {", ".maker {", ".step {", ".theme-pick {", ".pill-pick {", ".phone {", ".ready {",
            ".profile {", ".badge-card {", ".bare-card {", ".auth-scene {", ".auth-card {", ".faces {", ".field-error {",
        })
        {
            Assert.Contains(selector, screens);
        }
    }

    [Fact]
    public async Task Nao_ha_seletor_de_IA_nem_o_token_lilas_em_uso()
    {
        var css = await CssAsync();
        var screens = Split(css, ScreensLayer()).Inside + Split(css, MotionLayer()).Inside;

        Assert.DoesNotMatch(@"var\(--ai", screens);
        Assert.DoesNotMatch(@"\.(ai-|rcard|btn-ai|tag-ai|note\.ai)", screens);
    }

    [Fact]
    public async Task Todo_movimento_das_telas_fica_sob_movimento_nao_reduzido()
    {
        var css = await CssAsync();
        var (guarded, motionRest) = Split(Split(css, MotionLayer()).Inside, NoPreference());
        var screens = Split(css, ScreensLayer()).Inside;
        var screensRest = Split(screens, NoPreference()).Outside;

        // Na camada motion, fora do no-preference: só @keyframes (inertes sem animation) e as view transitions da casca.
        var motionUnguarded = string.Join('\n', Split(motionRest, Keyframes()).Outside.Split('\n')
            .Where(l => !l.Contains("view-transition", StringComparison.Ordinal)));
        Assert.DoesNotMatch(@"\banimation(-name)?\s*:\s*(?!none)", motionUnguarded);
        Assert.DoesNotMatch(@"\b(transition|translate|scale|rotate|transform)\s*:", motionUnguarded);
        Assert.DoesNotMatch(@"\bopacity\s*:\s*0(?![.\d])", motionUnguarded);

        // Na camada screens: nenhuma animação e nenhuma transição com duração fixa (os tokens --dur* zeram no reduce).
        Assert.DoesNotMatch(@"\banimation(-name)?\s*:\s*(?!none)", screensRest);
        var fixedDurations = Transition().Matches(screensRest)
            .Where(t => Duration().Matches(t.Value).Any(d => double.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture) > 0))
            .Select(t => t.Value)
            .ToList();
        Assert.Empty(fixedDurations);

        // O movimento destas telas está, de fato, dentro do no-preference.
        foreach (var rule in new[]
        {
            ".auth-card { animation: card-in", ".auth-scene::after { animation: aurora", ".face { animation: focus-in",
            ".btn .spin { animation: spin", ".tp:hover { translate:", ".field label { transition:",
        })
        {
            Assert.Contains(rule, guarded);
        }

        Assert.Matches(@"\.face \{[^}]*\btransition\s*:", guarded);

        // O zoom do personagem no hover só existe com mouse (toque não deixa o hover preso).
        Assert.Contains("@media (hover: hover) { .face:not(.lead):hover", screens);
    }
}
