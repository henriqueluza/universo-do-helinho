namespace Helinho.Application.Identidade;

public enum CredentialOutcome
{
    /// <summary>Senha errada, usuário inexistente ou conta bloqueada: indistinguíveis de fora (T3).</summary>
    Invalid,
    Valid,
}

public sealed record CredentialResult(CredentialOutcome Outcome, Guid UserId, bool IsStudent)
{
    public static CredentialResult Rejected { get; } = new(CredentialOutcome.Invalid, Guid.Empty, false);
}

/// <summary>Porta para o Identity: resolve matrícula, usuário ou e-mail (P1) e confere a senha, com bloqueio por tentativas.</summary>
public interface ILoginIdentityGateway
{
    Task<CredentialResult> VerifyAsync(string login, string password, CancellationToken cancellationToken);
}
