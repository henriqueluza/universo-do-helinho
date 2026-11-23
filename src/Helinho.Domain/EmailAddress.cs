using System.Text.RegularExpressions;

namespace Helinho.Domain;

/// <summary>
/// Validação de endereço de e-mail (ADR-0003 §9): formato, até 254 caracteres, só ASCII, sem espaço nem caractere de
/// controle. Lista de permitidos, de propósito mais estreita que a RFC: não aceita nome de exibição, aspas nem lista.
/// </summary>
public static partial class EmailAddress
{
    public const int MaxLength = 254;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaxLength } && Pattern().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]{1,64}@[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+\z")]
    private static partial Regex Pattern();
}
