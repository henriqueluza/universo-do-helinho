using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Helinho.Infrastructure.Email;

namespace Helinho.UnitTests;

/// <summary>Contraste AA (WCAG 2.x), critério da #205 e da #207: cada texto visível do HTML renderizado, com a cor e o fundo que herda. Texto grande (24 px, ou 18,66 px em negrito) pede 3:1; o resto, 4,5:1.</summary>
public class EmailContrastTests
{
    private sealed record Style(string Color, string Background, double FontPx, int Weight, bool Hidden);

    private sealed record TextRun(string Text, string Color, string Background, double Ratio, double Minimum)
    {
        public override string ToString() => $"{Color} sobre {Background}: {Ratio:0.00}:1, mínimo {Minimum}:1, em \"{Text.Trim()}\"";
    }

    [Theory]
    [MemberData(nameof(EmailTemplateRendererTests.Scenarios), MemberType = typeof(EmailTemplateRendererTests))]
    public void Todo_texto_visivel_tem_contraste_AA_sobre_o_fundo_que_herda(EmailScenario scenario)
    {
        var runs = TextRuns(EmailTemplateRendererTests.Renderer.Render(scenario.Model).Html);
        Assert.All(runs, run => Assert.True(run.Ratio >= run.Minimum, run.ToString()));
        Assert.Contains(runs, run => run is { Color: "#1C2C41", Background: "#FFFFFF" });
        Assert.Contains(runs, run => run is { Color: "#5A6472", Background: "#F8FAFD" });
        Assert.Equal(scenario.HasLink, runs.Any(run => run is { Color: "#FFFFFF", Background: "#005497" }));
    }

    [Theory]
    [InlineData("#5A6472", "#A0A8B2")]
    [InlineData("#1C2C41", "#DEE6EC")]
    [InlineData("#005497", "#7FA8D0")]
    public void Cor_trocada_por_uma_de_baixo_contraste_e_reprovada(string original, string weak)
    {
        var html = EmailTemplateRendererTests.Renderer.Render(new PasswordResetEmail(Guid.NewGuid(), "tok", TimeSpan.FromHours(1))).Html;
        Assert.Contains(original, html, StringComparison.Ordinal);
        Assert.Contains(TextRuns(html.Replace(original, weak, StringComparison.Ordinal)), run => run.Ratio < run.Minimum);
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.00)]
    [InlineData("#FFFFFF", "#005497", 7.73)]
    public void Razao_de_contraste_segue_a_formula_da_WCAG(string foreground, string background, double expected)
    {
        Assert.Equal((expected, expected), (Math.Round(ContrastRatio(foreground, background), 2), Math.Round(ContrastRatio(background, foreground), 2)));
    }

    /// <summary>Lê o corpo como um cliente de e-mail: cor, fundo, tamanho e peso vêm do ancestral mais próximo que os declara; a imagem conta pelo texto alternativo.</summary>
    private static List<TextRun> TextRuns(string html)
    {
        var body = html[html.IndexOf("<body", StringComparison.Ordinal)..(html.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length)];
        var stack = new Stack<(string Tag, Style Style)>([("", new Style("#000000", "#FFFFFF", 16, 400, false))]);
        var runs = new List<TextRun>();

        foreach (Match token in Regex.Matches(body, "<(/?)([a-z0-9]+)([^>]*)>|([^<]+)", RegexOptions.IgnoreCase))
        {
            var tag = token.Groups[2].Value.ToLowerInvariant();
            if (token.Groups[4].Success)
                Add(runs, stack.Peek().Style, WebUtility.HtmlDecode(token.Groups[4].Value));
            else if (token.Groups[1].Value == "/")
                Assert.Equal(tag, stack.Pop().Tag);
            else if (tag == "img")
                Add(runs, Apply(stack.Peek().Style, tag, token.Groups[3].Value), Regex.Match(token.Groups[3].Value, "alt=\"([^\"]*)\"").Groups[1].Value);
            else if (tag is not ("br" or "hr" or "meta"))
                stack.Push((tag, Apply(stack.Peek().Style, tag, token.Groups[3].Value)));
        }

        Assert.Single(stack);
        return runs;
    }

    private static Style Apply(Style parent, string tag, string attributes)
    {
        string? Find(string pattern) => Regex.Match(attributes, pattern, RegexOptions.IgnoreCase) is { Success: true } match ? match.Groups[1].Value.ToUpperInvariant() : null;

        var size = Find(@"font-size:\s*(\d+(?:\.\d+)?)px");
        var weight = Find(@"font-weight:\s*(\d+)");
        return new Style(
            Find(@"(?<![-\w])color:\s*(#[0-9a-f]{6})") ?? (tag == "a" ? "#0000EE" : parent.Color),
            Find(@"background-color:\s*(#[0-9a-f]{6})") ?? Find("bgcolor=\"(#[0-9a-f]{6})\"") ?? parent.Background,
            size is null ? parent.FontPx : double.Parse(size, CultureInfo.InvariantCulture),
            weight is not null ? int.Parse(weight, CultureInfo.InvariantCulture) : tag is "strong" or "b" or "h1" ? 700 : parent.Weight,
            parent.Hidden || Regex.IsMatch(attributes, @"display:\s*none", RegexOptions.IgnoreCase));
    }

    private static void Add(List<TextRun> runs, Style style, string text)
    {
        if (!style.Hidden && !string.IsNullOrWhiteSpace(text))
            runs.Add(new TextRun(text, style.Color, style.Background, ContrastRatio(style.Color, style.Background), style.FontPx >= 24 || (style.FontPx >= 18.66 && style.Weight >= 700) ? 3.0 : 4.5));
    }

    private static double ContrastRatio(string first, string second) =>
        (Math.Max(Luminance(first), Luminance(second)) + 0.05) / (Math.Min(Luminance(first), Luminance(second)) + 0.05);

    private static double Luminance(string hex) => new[] { (Offset: 1, Weight: 0.2126), (Offset: 3, Weight: 0.7152), (Offset: 5, Weight: 0.0722) }.Sum(channel =>
    {
        var c = int.Parse(hex.AsSpan(channel.Offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return channel.Weight * (c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
    });
}
