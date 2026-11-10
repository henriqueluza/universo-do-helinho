using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Identidade;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    Forbidden,
}

public sealed record LoginResult(LoginStatus Status, LoginResponse? Response)
{
    public static LoginResult Invalid { get; } = new(LoginStatus.InvalidCredentials, null);

    public static LoginResult Denied { get; } = new(LoginStatus.Forbidden, null);
}

/// <summary>Caso de uso do POST /login (P1, P2, P4).</summary>
public sealed class LoginHandler(
    ILoginIdentityGateway identity, IAppDbContext db, IClock clock, ApiAuthOptions options)
{
    private const int MaxFieldLength = 256;

    public async Task<LoginResult> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password)
            || request.Login.Length > MaxFieldLength || request.Password.Length > MaxFieldLength)
        {
            return LoginResult.Invalid;
        }

        var credential = await identity.VerifyAsync(request.Login.Trim(), request.Password, cancellationToken);
        if (credential.Outcome != CredentialOutcome.Valid)
        {
            return LoginResult.Invalid;
        }

        // Administrador e master não usam a API do jogo; aluno sem aprovação do responsável também não (P4).
        var student = credential.IsStudent
            ? await db.Students.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == credential.UserId, cancellationToken)
            : null;
        if (student is null || !student.GuardianApproved)
        {
            return LoginResult.Denied;
        }

        var token = SessionTokens.Generate();
        var expiresAt = clock.UtcNow + options.TokenLifetime;
        db.ApiSessions.Add(new ApiSession(credential.UserId, SessionTokens.Hash(token), expiresAt));
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResult(
            LoginStatus.Success, new LoginResponse(token, expiresAt.UtcDateTime, student.Id.ToString()));
    }
}
