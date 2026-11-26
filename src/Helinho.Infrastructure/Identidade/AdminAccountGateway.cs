using Helinho.Application.Administradores;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Contas de administrador sobre o Identity. O lock da unidade e o <c>UserManager</c> usam o mesmo <see cref="AppDbContext"/> (mesma transação).</summary>
public sealed class AdminAccountGateway(AppDbContext db, UserManager<AppUser> users) : IAdminAccountGateway
{
    public async Task<IReadOnlyList<Unit>> ListUnitsAsync(CancellationToken cancellationToken) =>
        await db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdminAccount>> ListAdminsAsync(CancellationToken cancellationToken) =>
        (await users.GetUsersInRoleAsync(RoleNames.Admin))
            .Select(a => new AdminAccount(a.UserName ?? "", a.Email ?? "", a.UnitId, a.LockoutEnd))
            .ToList();

    public async Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken) =>
        new UnitLock(await db.BeginLockedTransactionAsync($"admin-unit:{unitId}", cancellationToken));

    public async Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = users.NormalizeEmail(email.Trim());
        var lockKey = $"account-email:{normalized}";
        // Lock de transação: dois cadastros paralelos com o mesmo e-mail, em unidades diferentes, não passam juntos.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        return await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken)
    {
        var user = new AppUser { UserName = account.UserName.Trim(), Email = account.Email.Trim(), UnitId = account.UnitId };
        var result = await users.CreateAsync(user, account.Password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, RoleNames.Admin);
        }

        return result.Succeeded ? [] : result.Errors.Select(e => e.Code).ToList();
    }

    private sealed class UnitLock(IDbContextTransaction transaction) : IAdminUnitLock
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
