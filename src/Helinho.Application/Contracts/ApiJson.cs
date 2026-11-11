using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helinho.Application.Contracts;

/// <summary>Opções de JSON do contrato com o jogo: snake_case, enums como texto, sem null (regras do JsonUtility).</summary>
public static class ApiJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
