using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Microsoft.Extensions.Options;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Web.Panel;

public sealed record PanelUserModel(string Name, string Role, string Unit, IReadOnlyList<NavItem> Items)
{
    /// <summary>Iniciais para o avatar: primeira letra do primeiro e do último nome.</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => char.ToUpperInvariant(parts[0][0]).ToString(),
                _ => string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0])),
            };
        }
    }

    /// <summary>Itens agrupados na ordem do menu; o grupo inicial vem com rótulo nulo.</summary>
    public IEnumerable<IGrouping<string?, NavItem>> Groups => Items.GroupBy(i => i.Group);
}

/// <summary>Dados do menu lateral: itens permitidos ao papel, nome, papel e unidade do usuário (PRD B1).</summary>
public sealed class PanelUserViewComponent(ICurrentUser currentUser, IAppDbContext db, IOptions<AiOptions> ai) : ViewComponent
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
            NavMenu.For(currentUser.IsInRole, ai.Value.IsEnabled));
        return View(model);
    }
}
