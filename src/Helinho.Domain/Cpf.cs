namespace Helinho.Domain;

/// <summary>Validação de CPF (11 dígitos, dígitos verificadores, sem sequência repetida).</summary>
public static class Cpf
{
    public static bool IsValid(string? value)
    {
        var digits = Digits(value);
        if (digits.Length != 11 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        return Check(digits, 9) == digits[9] - '0' && Check(digits, 10) == digits[10] - '0';
    }

    /// <summary>Formato canônico 000.000.000-00; devolve o texto limpo se não tiver 11 dígitos.</summary>
    public static string Format(string? value)
    {
        var d = Digits(value);
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : (value ?? "").Trim();
    }

    private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());

    private static int Check(string digits, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += (digits[i] - '0') * (length + 1 - i);
        }

        var rest = sum * 10 % 11;
        return rest == 10 ? 0 : rest;
    }
}
