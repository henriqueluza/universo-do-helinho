namespace Helinho.Application.Abstractions;

/// <summary>Aluno dono de uma sessão válida da API.</summary>
public sealed record ValidatedSession(Guid StudentId);

/// <summary>Porta que valida o token Bearer da API do jogo. A frente de identidade fornece a implementação real.</summary>
public interface IApiSessionValidator
{
    /// <summary>Devolve a sessão, ou null se o token for desconhecido, vencido ou revogado.</summary>
    Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken);
}
