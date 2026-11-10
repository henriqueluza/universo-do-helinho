using Helinho.Domain;

namespace Helinho.Web.Panel;

public sealed record NavItem(string Label, string Path, string IconPath, bool MasterOnly = false);

/// <summary>Itens do menu lateral (PRD B1). Unidades e Administradores só para o master.</summary>
public static class NavMenu
{
    // Ícones: traçados SVG de 16 px (viewBox 16), traço 1,5.
    private static readonly NavItem[] All =
    [
        new("Dashboard", "/Dashboard", "M2 9.5h5v4.5H2zM9 2h5v12H9zM2 2h5v5H2z"),
        new("Alunos", "/Alunos", "M8 7.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM3 14c0-2.8 2.2-4.5 5-4.5s5 1.7 5 4.5"),
        new("Unidades", "/Unidades", "M2 14V6l6-4 6 4v8zM6.5 14v-4h3v4", MasterOnly: true),
        new("Administradores", "/Administradores", "M8 1.5l5 2v4c0 3-2 5.5-5 7-3-1.5-5-4-5-7v-4z", MasterOnly: true),
        new("Questões", "/Questoes", "M6 6a2 2 0 1 1 3 1.7c-.7.4-1 .8-1 1.6M8 12v.5M2 2h12v12H2z"),
        new("Eventos", "/Eventos", "M2 3.5h12V14H2zM5 1.5v3M11 1.5v3M2 7h12"),
        new("Missões", "/Missoes", "M3 14V2M3 3h9l-2 3 2 3H3"),
        new("Desempenho", "/Desempenho", "M2 14h12M4 14V9M8 14V4M12 14V7"),
    ];

    public static IReadOnlyList<NavItem> For(Func<string, bool> isInRole) =>
        All.Where(i => !i.MasterOnly || isInRole(RoleNames.Master)).ToList();
}
