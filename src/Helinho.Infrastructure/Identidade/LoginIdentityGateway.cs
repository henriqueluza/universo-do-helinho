using Helinho.Application.Identidade;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Resolve matrícula, usuário ou e-mail e confere a senha com o Identity.</summary>
public sealed class LoginIdentityGateway(AppDbContext db, UserManager<AppUser> users) : ILoginIdentityGateway
{
    // Hash descartável: usuário inexistente paga o mesmo custo de verificação que senha errada (T3).
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), "dummy-Password-1");

    public async Task<CredentialResult> VerifyAsync(string login, string password, CancellationToken cancellationToken)
    {
        var user = await FindAsync(login, cancellationToken);
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash, password);
            return CredentialResult.Rejected;
        }

        if (!user.LockoutEnabled)
        {
            await users.SetLockoutEnabledAsync(user, true);
        }

        if (await users.IsLockedOutAsync(user))
        {
            users.PasswordHasher.VerifyHashedPassword(user, DummyHash, password);
            return CredentialResult.Rejected;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return CredentialResult.Rejected;
        }

        await users.ResetAccessFailedCountAsync(user);
        var isStudent = await users.IsInRoleAsync(user, RoleNames.Student);
        return new CredentialResult(CredentialOutcome.Valid, user.Id, isStudent);
    }

    private async Task<AppUser?> FindAsync(string login, CancellationToken cancellationToken)
    {
        // ignora-filtro: login ocorre antes de haver escopo de unidade; a busca é pela matrícula informada.
        var byEnrollment = await db.Students.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Enrollment == login).Select(s => s.UserId).FirstOrDefaultAsync(cancellationToken);
        if (byEnrollment != Guid.Empty)
        {
            return await users.FindByIdAsync(byEnrollment.ToString());
        }

        return await users.FindByNameAsync(login) ?? await users.FindByUniqueEmailAsync(login, cancellationToken);
    }
}
