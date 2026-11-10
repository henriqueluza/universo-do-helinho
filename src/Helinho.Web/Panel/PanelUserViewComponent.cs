using Helinho.Application.Abstractions;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Web.Panel;

public sealed record PanelUserModel(string Name, string Role, string Unit, IReadOnlyList<NavItem> Items);

/// <summary>Dados do menu lateral: itens permitidos ao papel, nome, papel e unidade do usuário (PRD B1).</summary>
public sealed class PanelUserViewComponent(ICurrentUser currentUser, IAppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var isMaster = currentUser.IsInRole(RoleNames.Master);
        var unit = "Todas as unidades";
        if (!isMaster && currentUser.UnitId is { } unitId)
        {
            unit = await db.Units.AsNoTracking().Where(u => u.Id == unitId)
                .Select(u => u.Name).FirstOrDefaultAsync(HttpContext.RequestAborted) ?? "Sem unidade";
        }

        var model = new PanelUserModel(
            HttpContext.User.Identity?.Name ?? "Usuário",
            isMaster ? "Master" : "Administrador de unidade",
            unit,
            NavMenu.For(currentUser.IsInRole));
        return View(model);
    }
}
