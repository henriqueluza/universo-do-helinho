using System.Security.Cryptography;
using System.Text;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Identidade;

public sealed class StudentAccounts(AppDbContext db) : IStudentAccounts
{
    public async Task<Guid> StageAsync(Guid unitId, string enrollment, CancellationToken cancellationToken)
    {
        var userName = enrollment.ToLowerInvariant();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            UnitId = unitId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        };
        var roleId = await db.Roles.Where(r => r.Name == RoleNames.Student).Select(r => r.Id).SingleAsync(cancellationToken);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
        return user.Id;
    }

    public Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.PasswordHash != null, cancellationToken);

    public async Task<string> RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N"); // como o UserStore: outra gravação da mesma conta em curso perde
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp)))[..16];
    }
}
