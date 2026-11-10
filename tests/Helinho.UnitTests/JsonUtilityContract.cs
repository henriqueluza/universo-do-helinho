using System.Text.Json;
using System.Text.RegularExpressions;

namespace Helinho.UnitTests;

/// <summary>Verificador das 8 regras do JsonUtility (rotas-api.md, seção 1). Devolve as violações encontradas.</summary>
public static partial class JsonUtilityContract
{
    private static readonly string[] QuizTypes = ["ambiental", "conhecimento", "esportivo", "cultural", "diversidade"];

    public static IReadOnlyList<string> Verify(string json)
    {
        var violations = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            violations.Add("1.1 raiz não é objeto");
            return violations;
        }

        Walk(doc.RootElement, "$", violations);
        return violations;
    }

    private static void Walk(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var props = element.EnumerateObject().ToList();
                if (props.Count > 1 && props.All(p => p.Value.ValueKind == JsonValueKind.Object))
                {
                    violations.Add($"1.2 {path}: parece dicionário (todos os valores são objetos)");
                }

                foreach (var p in props)
                {
                    if (!SnakeCase().IsMatch(p.Name))
                    {
                        violations.Add($"1.3 {path}.{p.Name}: chave fora de snake_case");
                    }

                    if (QuizTypes.Contains(p.Name))
                    {
                        violations.Add($"1.2 {path}.{p.Name}: chave dinâmica (valor de domínio usado como chave)");
                    }

                    Walk(p.Value, $"{path}.{p.Name}", violations);
                }

                break;
            case JsonValueKind.Array:
                foreach (var (item, i) in element.EnumerateArray().Select((e, i) => (e, i)))
                {
                    if (item.ValueKind == JsonValueKind.Array)
                    {
                        violations.Add($"1.8 {path}[{i}]: array dentro de array");
                    }

                    Walk(item, $"{path}[{i}]", violations);
                }

                break;
            case JsonValueKind.Null:
                violations.Add($"1.4 {path}: null");
                break;
            case JsonValueKind.String:
                var s = element.GetString()!;
                if (DateLike().IsMatch(s) && !UtcIso().IsMatch(s))
                {
                    violations.Add($"1.5 {path}: data fora de ISO 8601 UTC com Z ({s})");
                }

                break;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex DateLike();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$")]
    private static partial Regex UtcIso();
}
