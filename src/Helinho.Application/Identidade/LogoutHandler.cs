using Helinho.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Identidade;

/// <summary>Caso de uso do POST /logout (P3): revoga no servidor só a sessão do token apresentado.</summary>
public sealed class LogoutHandler(IAppDbContext db, IClock clock)
{
    /// <summary>Devolve false se o token não corresponde a uma sessão ativa (desconhecido, vencido ou já revogado).</summary>
    public async Task<bool> HandleAsync(string token, CancellationToken cancellationToken)
    {
        var hash = SessionTokens.Hash(token);
        var now = clock.UtcNow;
        var session = await db.ApiSessions.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null || !session.IsActive(now))
        {
            return false;
        }

        session.Revoke(now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
