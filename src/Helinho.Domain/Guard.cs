namespace Helinho.Domain;

internal static class Guard
{
    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{field} é obrigatório.") : value.Trim();

    public static Guid Required(Guid value, string field) =>
        value == Guid.Empty ? throw new DomainException($"{field} é obrigatório.") : value;
}
