using System.Text;
using System.Text.RegularExpressions;

namespace Helinho.IntegrationTests;

/// <summary>CSS das telas internas e de conta do tema Recreio (Issue #144): camadas, seletores-chave, sem IA, movimento reduzido.</summary>
[Collection(PostgresCollection.Name)]
public partial class TemaTelasCssTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"@layer\s+(screens|motion)\s*\{")]
    private static partial Regex LayerOpen();

    private async Task<string> CssAsync()
    {
        await using var factory = new HelinhoWebFactory(postgres);
        await factory.EnsureDatabaseAsync();
        using var client = factory.CreateAnonymousClient();
        return await client.GetStringAsync("/css/theme.css", Ct);
    }

    /// <summary>Concatena o corpo de todos os blocos <c>@layer nome { ... }</c> (com chaves balanceadas).</summary>
    private static string Layer(string css, string name)
    {
        var sb = new StringBuilder();
        foreach (Match m in LayerOpen().Matches(css))
        {
            if (m.Groups[1].Value != name)
            {
                continue;
            }

            var depth = 1;
            var i = m.Index + m.Length;
            var start = i;
            while (i < css.Length && depth > 0)
            {
                depth += css[i] == '{' ? 1 : css[i] == '}' ? -1 : 0;
                i++;
            }

            sb.Append(css, start, i - start - 1).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public async Task Seletores_das_telas_ficam_na_camada_screens()
    {
        var screens = Layer(await CssAsync(), "screens");

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
    public async Task Seletores_e_movimento_do_Painel_ficam_nas_camadas_certas()
    {
        var css = await CssAsync();
        var screens = Layer(css, "screens");
        var motion = Layer(css, "motion");

        foreach (var selector in new[] { ".scene {", ".kpis {", ".kpi {", ".linechart {", ".dtable {", ".rank {", ".heat {", ".cast {", ".feed {", ".tip {", ".empty {" })
        {
            Assert.Contains(selector, screens);
        }

        foreach (var keyframes in new[] { "@keyframes wipe", "@keyframes grow-x", "@keyframes cell-in", "@keyframes shimmer" })
        {
            Assert.Contains(keyframes, motion);
        }
    }

    [Fact]
    public async Task Nao_ha_seletor_de_IA_nem_o_token_lilas_em_uso()
    {
        var css = await CssAsync();
        var screens = Layer(css, "screens") + Layer(css, "motion");

        Assert.DoesNotMatch(@"var\(--ai", screens);
        Assert.DoesNotMatch(@"\.(ai-|rcard|btn-ai|tag-ai|note\.ai)", screens);
    }

    [Fact]
    public async Task Todo_movimento_das_telas_fica_sob_movimento_nao_reduzido()
    {
        var motion = Layer(await CssAsync(), "motion");
        StringBuilder guarded = new(), rest = new();
        var idx = 0;
        while (true)
        {
            var found = motion.IndexOf("@media (prefers-reduced-motion: no-preference) {", idx, StringComparison.Ordinal);
            if (found < 0)
            {
                rest.Append(motion, idx, motion.Length - idx);
                break;
            }

            rest.Append(motion, idx, found - idx);
            idx = found;
            var depth = 0;
            var i = motion.IndexOf('{', idx);
            var start = i;
            do
            {
                depth += motion[i] == '{' ? 1 : motion[i] == '}' ? -1 : 0;
                i++;
            }
            while (depth > 0);
            guarded.Append(motion, start, i - start);
            idx = i;
        }

        var unguarded = string.Join('\n', rest.ToString().Split('\n').Where(l => !l.Contains("view-transition", StringComparison.Ordinal)));
        Assert.Contains(".auth-card", guarded.ToString());
        Assert.DoesNotMatch(@"\banimation\s*:\s*(?!none)", unguarded);
    }
}
