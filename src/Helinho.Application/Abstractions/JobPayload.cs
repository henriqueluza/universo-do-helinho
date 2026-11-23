using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Helinho.Application.Abstractions;

/// <summary>
/// Parâmetros do trabalho, versionados (<c>{"v":1,…}</c>). Por construção não cabe texto livre (ADR-0003 §5.1 e §6.8 A5):
/// só identificadores, inteiros, booleanos e códigos de vocabulário fechado. Dados próprios do trabalho ficam em
/// outro registro, e o payload leva só o id dele.
/// </summary>
public sealed partial class JobPayload
{
    private readonly JsonObject _json;

    public JobPayload(int version = 1) =>
        _json = new JsonObject { ["v"] = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version)) };

    public JobPayload WithId(string name, Guid id) => Set(name, JsonValue.Create(id));

    public JobPayload WithNumber(string name, int number) => Set(name, JsonValue.Create(number));

    public JobPayload WithFlag(string name, bool flag) => Set(name, JsonValue.Create(flag));

    /// <summary>Código vindo de constante do código-fonte (<c>resend</c>, <c>auto-approval</c>), nunca de entrada de alguém.</summary>
    public JobPayload WithCode(string name, string code) => Set(name, JsonValue.Create(JobCode.Require(code, nameof(code))));

    public string ToJson() => _json.ToJsonString();

    private JobPayload Set(string name, JsonNode value)
    {
        if (name == "v" || !NamePattern().IsMatch(name))
        {
            throw new ArgumentException("Nome de parâmetro inválido.", nameof(name));
        }

        _json[name] = value;
        return this;
    }

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]{0,29}\z")]
    private static partial Regex NamePattern();
}

/// <summary>Código de vocabulário fechado (<c>resend</c>, <c>http_429</c>, <c>expired</c>): minúsculas, dígitos e separadores, até 30 caracteres.</summary>
public static partial class JobCode
{
    public static string Require(string code, string parameter) =>
        code is { Length: <= 30 } && Pattern().IsMatch(code)
            ? code : throw new ArgumentException("Código fora do vocabulário fechado: texto livre não entra na fila.", parameter);

    [GeneratedRegex(@"^[a-z][a-z0-9]*([._-][a-z0-9]+)*\z")]
    private static partial Regex Pattern();
}
