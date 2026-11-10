using System.Reflection;
using System.Text.Json;
using Helinho.Application.Contracts;

namespace Helinho.UnitTests;

public class ApiContractTests
{
    private static readonly Assembly ContractAssembly = typeof(ApiJson).Assembly;

    public static TheoryData<Type> ResponseTypes() =>
        new(ContractAssembly.GetTypes()
            .Where(t => t.Namespace == typeof(ApiJson).Namespace && t.IsClass && t.Name.EndsWith("Response", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(ResponseTypes))]
    public void DTO_de_resposta_respeita_as_regras_do_JsonUtility(Type type)
    {
        var json = JsonSerializer.Serialize(Sample(type), type, ApiJson.CreateOptions());

        Assert.Empty(JsonUtilityContract.Verify(json));
        AssertEnumsAreText(type, JsonDocument.Parse(json).RootElement);
        AssertNoNullableProperties(type);
    }

    [Fact]
    public void Verificador_rejeita_violacoes()
    {
        Assert.NotEmpty(JsonUtilityContract.Verify("[{\"a\":1}]"));
        Assert.NotEmpty(JsonUtilityContract.Verify("{\"ambiental\":{\"a\":1},\"cultural\":{\"a\":2}}"));
        Assert.NotEmpty(JsonUtilityContract.Verify("{\"displayName\":\"x\"}"));
        Assert.NotEmpty(JsonUtilityContract.Verify("{\"a\":null}"));
        Assert.NotEmpty(JsonUtilityContract.Verify("{\"ends_at\":\"2026-10-24T23:59:00+00:00\"}"));
        Assert.NotEmpty(JsonUtilityContract.Verify("{\"a\":[[1]]}"));
        Assert.Empty(JsonUtilityContract.Verify("{\"ends_at\":\"2026-10-24T23:59:00Z\",\"a\":[{\"b\":1}]}"));
    }

    [Fact]
    public void Corpo_de_erro_usa_error_e_message()
    {
        var json = JsonSerializer.Serialize(ApiErrors.CreateTokenExpired(), ApiJson.CreateOptions());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(["error", "message"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("token_expired", doc.RootElement.GetProperty("error").GetString());
        Assert.Empty(JsonUtilityContract.Verify(json));
    }

    private static void AssertEnumsAreText(Type type, JsonElement element)
    {
        foreach (var prop in type.GetProperties().Where(p => p.PropertyType.IsEnum))
        {
            var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(prop.Name);
            Assert.Equal(JsonValueKind.String, element.GetProperty(name).ValueKind);
        }
    }

    private static void AssertNoNullableProperties(Type type)
    {
        var nullability = new NullabilityInfoContext();
        foreach (var prop in type.GetProperties())
        {
            Assert.NotEqual(NullabilityState.Nullable, nullability.Create(prop).ReadState);
        }
    }

    private static object Sample(Type type)
    {
        var ctor = type.GetConstructors().Single();
        return ctor.Invoke(ctor.GetParameters().Select(p => SampleValue(p.ParameterType)).ToArray());
    }

    private static object SampleValue(Type t)
    {
        if (t == typeof(string))
        {
            return "texto";
        }

        if (t == typeof(int))
        {
            return 1;
        }

        if (t == typeof(double))
        {
            return 0.5;
        }

        if (t == typeof(bool))
        {
            return true;
        }

        if (t == typeof(DateTime))
        {
            return new DateTime(2026, 10, 24, 23, 59, 0, DateTimeKind.Utc);
        }

        if (t.IsEnum)
        {
            return Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)!;
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var item = t.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!;
            list.Add(SampleValue(item));
            return list;
        }

        return Sample(t);
    }
}
