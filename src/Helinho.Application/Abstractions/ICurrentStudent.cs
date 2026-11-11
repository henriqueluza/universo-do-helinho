namespace Helinho.Application.Abstractions;

/// <summary>Aluno autenticado na API do jogo, para os casos de uso.</summary>
public interface ICurrentStudent
{
    /// <summary>Identificador do aluno; lança se a requisição não estiver autenticada.</summary>
    Guid StudentId { get; }
}
