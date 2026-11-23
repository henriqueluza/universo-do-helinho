using System.Text;
using System.Text.Json;

namespace Helinho.Application.Ai;

/// <summary>
/// Análise estrita do JSON que um modelo devolve (ADR-0004 §6.3 e §8): <c>{"questoes":[{enunciado,a,b,c,d,correta,explicacao}]}</c>.
/// O texto é dado não confiável. JSON inválido ou fora do formato invalida o lote; item fora do formato vira
/// <see cref="MalformedItem"/>, que o validador descarta e conta.
/// </summary>
public static class QuestionDraftOutputParser
{
    public const int MaxBytes = 64 * 1024;

    /// <summary>
    /// Marca de item fora do formato. Não carrega nada do texto recebido. Reconheça com <c>ReferenceEquals</c>: por ser
    /// record, <c>==</c> também a igualaria a qualquer rascunho vazio com índice -1.
    /// </summary>
    public static readonly QuestionDraft MalformedItem = new("", [], -1, "");

    private static readonly string[] OptionKeys = ["a", "b", "c", "d"];
    private static readonly string[] ItemKeys = ["enunciado", .. OptionKeys, "correta", "explicacao"];

    // Raiz, lista, item e um nível a mais: o item com valor aninhado é descartado sozinho; mais fundo que isso, o lote cai.
    private static readonly JsonDocumentOptions Strict = new() { MaxDepth = 4 };

    /// <exception cref="QuestionDraftGenerationException"><see cref="QuestionDraftFailure.InvalidOutput"/>, sem o texto recebido.</exception>
    public static IReadOnlyList<QuestionDraft> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || Encoding.UTF8.GetByteCount(output) > MaxBytes)
        {
            throw new QuestionDraftGenerationException(QuestionDraftFailure.InvalidOutput);
        }

        try
        {
            using var document = JsonDocument.Parse(output, Strict);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("questoes", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new QuestionDraftGenerationException(QuestionDraftFailure.InvalidOutput);
            }

            return [.. items.EnumerateArray().Select(ToDraft)];
        }
        catch (JsonException)
        {
            // A JsonException cita trechos do texto: não segue como exceção interna.
            throw new QuestionDraftGenerationException(QuestionDraftFailure.InvalidOutput);
        }
    }

    private static QuestionDraft ToDraft(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return MalformedItem;
        }

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            // Propriedade desconhecida, repetida ou que não é texto invalida o item.
            var key = Key(property);
            if (key is null || property.Value.ValueKind != JsonValueKind.String || !fields.TryAdd(key, Text(property.Value)))
            {
                return MalformedItem;
            }
        }

        if (fields.Count != ItemKeys.Length || fields.Values.Any(value => value is null))
        {
            return MalformedItem;
        }

        // Fora de a–d, o índice fica -1 e o validador recusa. Sem diferenciar maiúsculas (ADR-0004 §6.3).
        var correct = Array.FindIndex(OptionKeys, key => string.Equals(key, fields["correta"]?.Trim(), StringComparison.OrdinalIgnoreCase));
        return new QuestionDraft(fields["enunciado"] ?? "", [.. OptionKeys.Select(key => fields[key] ?? "")], correct, fields["explicacao"] ?? "");
    }

    /// <summary>A chave conhecida, ou <c>null</c>. Ler o nome também lança com escape de par substituto quebrado.</summary>
    private static string? Key(JsonProperty property)
    {
        try
        {
            return Array.Find(ItemKeys, known => property.NameEquals(known));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary><c>null</c> quando o texto tem escape de par substituto quebrado, que o <c>GetString</c> recusa lançando.</summary>
    private static string? Text(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
