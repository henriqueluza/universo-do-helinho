using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Web.Pages.Administradores;

public sealed class IndexModel(UserManager<AppUser> users, IAppDbContext db) : PageModel
{
    public IReadOnlyList<AdminRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
        var admins = await users.GetUsersInRoleAsync(RoleNames.Admin);
        var now = DateTimeOffset.UtcNow;
        Rows = admins
            .Select(a => new AdminRow(
                a.UserName ?? "", a.Email ?? "",
                a.UnitId is { } id && units.TryGetValue(id, out var name) ? name : "—",
                a.LockoutEnd is null || a.LockoutEnd <= now))
            .OrderBy(r => r.UnitName).ThenBy(r => r.UserName)
            .ToList();
    }

    public sealed record AdminRow(string UserName, string Email, string UnitName, bool Active);
}
