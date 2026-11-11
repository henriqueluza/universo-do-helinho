using System.Security.Claims;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Panel;

/// <summary>Acrescenta a unidade do usuário às claims emitidas no login (papéis já vêm do Identity).</summary>
public sealed class UnitClaimsPrincipalFactory(
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.UnitId is { } unitId)
        {
            identity.AddClaim(new Claim(AppClaims.UnitId, unitId.ToString()));
        }

        return identity;
    }
}
