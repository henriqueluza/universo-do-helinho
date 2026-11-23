using System.Security.Cryptography;
using System.Text;
using Helinho.Application.Email;
using Helinho.Application.Identidade;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Consulta do pedido de redefinição (ADR-0003 §11.1). Não usa <c>FindByEmailAsync</c>, que lança com e-mail repetido (#184).</summary>
public sealed class PasswordResetAccounts(AppDbContext db, ILookupNormalizer normalizer) : IPasswordResetAccounts
{
    private static readonly string[] PanelRoles = [RoleNames.Master, RoleNames.Admin];

    public async Task<ContaParaRedefinicao?> FindSingleAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = normalizer.NormalizeEmail(email.Trim());
        var found = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized && db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && PanelRoles.Contains(r.Name))))
            .Select(u => new { u.Id, u.SecurityStamp })
            .Take(2)
            .ToListAsync(cancellationToken);
        return found is [{ SecurityStamp: { } stamp } conta] ? new ContaParaRedefinicao(conta.Id, SecurityStampDigest.Of(stamp)) : null;
    }

    public Task<int> CountLiveAsync(Guid userId, string keyPrefix, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.QueueJobs.CountAsync(
            j => j.Type == EmailJobTypes.PasswordReset && j.SubjectId == userId && j.CreatedAt >= since
                && (j.Status == QueueJobStatus.Pending || j.Status == QueueJobStatus.Processing || j.Status == QueueJobStatus.Succeeded)
                && j.IdempotencyKey != null && j.IdempotencyKey.StartsWith(keyPrefix),
            cancellationToken);

    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Dois pedidos na mesma janela: o índice único recusou o segundo. Um 500 aqui denunciaria a conta.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

/// <summary>
/// Resumo do carimbo de segurança usado nas chaves de idempotência (ADR-0003 §5.4). Não é token nem permite gerar link:
/// só diz se dois pedidos foram feitos com o mesmo carimbo.
/// </summary>
public static class SecurityStampDigest
{
    public static string Of(string securityStamp) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp)))[..32];
}
