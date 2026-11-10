using Helinho.Application.Abstractions;
using Helinho.Application.Identidade;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Valida o token Bearer: compara o hash, confere validade e revogação e devolve o aluno dono.</summary>
public sealed class ApiSessionValidator(IAppDbContext db, IClock clock) : IApiSessionValidator
{
    public async Task<ValidatedSession?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var hash = SessionTokens.Hash(token);
        var session = await db.ApiSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null || !session.IsActive(clock.UtcNow))
        {
            return null;
        }

        var studentId = await db.Students.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == session.UserId && s.GuardianApproved)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
        return studentId is { } id ? new ValidatedSession(id) : null;
    }
}
