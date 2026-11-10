namespace Helinho.Domain;

/// <summary>Violação de invariante do domínio.</summary>
public sealed class DomainException(string message) : Exception(message);
