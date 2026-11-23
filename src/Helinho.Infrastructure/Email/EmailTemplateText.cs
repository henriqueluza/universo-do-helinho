using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Substituição segura dos modelos de e-mail (ADR-0003 §8.2, D10). Marcadores: <c>{{nome_em_minusculas}}</c>.
/// Todo valor é limpo e, no HTML, codificado; não existe marcador "cru". Marcador sem valor, valor sem marcador
/// ou marcador malformado é exceção, nunca texto vazio nem marcador no e-mail. As mensagens de erro citam o nome
/// do marcador, nunca o valor.
/// </summary>
public static partial class EmailTemplateText
{
    /// <summary>Para HTML, em texto ou em atributo entre aspas: todo valor passa pelo <see cref="HtmlEncoder"/>.</summary>
    public static string FillHtml(string template, IReadOnlyDictionary<string, string> values) =>
        Fill(template, values, HtmlEncoder.Default.Encode, allowUnused: false);

    /// <summary>Para texto puro e assunto: o valor entra cru, em uma linha só.</summary>
    public static string FillText(string template, IReadOnlyDictionary<string, string> values, bool allowUnused = false) =>
        Fill(template, values, value => value, allowUnused);

    private static string Fill(string template, IReadOnlyDictionary<string, string> values, Func<string, string> encode, bool allowUnused)
    {
        if (Stray().IsMatch(Marker().Replace(template, "")))
        {
            throw new InvalidOperationException("Marcador malformado no modelo: sobrou {{, }}, [[ ou ]].");
        }

        // Uma passada só: o valor substituído não é lido de novo.
        var used = new HashSet<string>(StringComparer.Ordinal);
        var filled = Marker().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            var value = Clean(values.GetValueOrDefault(name));
            used.Add(name);
            return value.Length > 0 ? encode(value) : throw new InvalidOperationException($"Marcador {{{{{name}}}}} desconhecido ou sem valor.");
        });

        var unused = values.Keys.Except(used).ToList();
        return allowUnused || unused.Count == 0
            ? filled
            : throw new InvalidOperationException($"Valor sem marcador no modelo: {string.Join(", ", unused)}.");
    }

    /// <summary>
    /// Uma linha só: controle e separadores de linha viram espaço (nada de cabeçalho ou parágrafo injetado), caracteres de
    /// formatação (bidi, largura zero, etiquetas invisíveis) saem, e chaves viram parênteses para o valor nunca parecer marcador,
    /// nem aqui nem no provedor. Percorre por runa: cobre os planos suplementares e troca substituto solto por U+FFFD.
    /// </summary>
    private static string Clean(string? value)
    {
        var runes = (value ?? "").EnumerateRunes()
            .Where(rune => Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Format)
            .Select(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                ? " "
                : rune.Value switch { '{' => "(", '}' => ")", _ => rune.ToString() });
        return string.Join(' ', string.Concat(runes).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"\{\{([a-z_]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"\{\{|\}\}|\[\[|\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex Stray();
}
