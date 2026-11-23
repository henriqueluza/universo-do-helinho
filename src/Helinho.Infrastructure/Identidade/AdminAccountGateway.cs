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
            .Select(a => new AdminAccount(a.Id, a.UserName ?? "", a.Email ?? "", a.UnitId, a.LockoutEnd, a.PasswordHash is not null))
            .ToList();

    public async Task<AdminAccount?> FindAdminAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await ListAdminsAsync(cancellationToken)).FirstOrDefault(a => a.Id == accountId);

    // Nos dois métodos abaixo o Identity confere o ConcurrencyStamp: se a conta ganhou senha depois de lida, a gravação falha.
    public async Task<bool> RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken) =>
        await FindInvitedAsync(accountId) is { } user && (await users.UpdateSecurityStampAsync(user)).Succeeded;

    public async Task<bool> DeleteInvitedAdminAsync(Guid accountId, CancellationToken cancellationToken) =>
        await FindInvitedAsync(accountId) is { } user && (await users.DeleteAsync(user)).Succeeded;

    private async Task<AppUser?> FindInvitedAsync(Guid accountId) =>
        await users.FindByIdAsync(accountId.ToString()) is { PasswordHash: null } user && await users.IsInRoleAsync(user, RoleNames.Admin)
            ? user : null;

    public async Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken) =>
        new UnitLock(db, await db.BeginLockedTransactionAsync($"admin-unit:{unitId}", cancellationToken));

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
        var user = new AppUser { Id = account.Id, UserName = account.UserName.Trim(), Email = account.Email.Trim(), UnitId = account.UnitId };
        var result = await users.CreateAsync(user); // sem senha: quem define é o convidado, pelo link (ADR-0003 §11.2)
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(user, RoleNames.Admin);
        }

        return result.Succeeded ? [] : result.Errors.Select(e => e.Code).ToList();
    }

    private sealed class UnitLock(AppDbContext db, IDbContextTransaction transaction) : IAdminUnitLock
    {
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await db.SaveChangesAsync(cancellationToken); // o convite que o caso de uso enfileirou entra na mesma transação
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
