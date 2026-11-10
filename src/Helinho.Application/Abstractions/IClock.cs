namespace Helinho.Application.Abstractions;

/// <summary>Relógio injetável, para tornar o tempo determinístico em testes.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
