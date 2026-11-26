using Helinho.Domain;

namespace Helinho.Web.Panel;

/// <summary>
/// Item do menu lateral. <paramref name="Icon"/> é o id do sprite <c>_Icones</c> (sem o '#');
/// <paramref name="Tile"/> é o índice (1 a 9) do token de cor <c>--tile-N</c> do ladrilho;
/// <paramref name="Group"/> é o rótulo do grupo (nulo = grupo inicial, sem rótulo).
/// </summary>
public sealed record NavItem(string Label, string Path, string Icon, int Tile, string? Group = null, bool MasterOnly = false);

/// <summary>Itens do menu lateral (PRD B1). Unidades e Administradores só para o master.</summary>
public static class NavMenu
{
    public const string GroupContent = "Conteúdo do jogo";
    public const string GroupManagement = "Gestão";

    private static readonly NavItem[] All =
    [
        new("Dashboard", "/Dashboard", "i-dash", 1),
        new("Alunos", "/Alunos", "i-alunos", 2),
        new("Desempenho", "/Desempenho", "i-desemp", 9),
        new("Questões", "/Questoes", "i-quest", 4, GroupContent),
        new("Missões", "/Missoes", "i-miss", 3, GroupContent),
        new("Eventos", "/Eventos", "i-event", 6, GroupContent),
        new("Unidades", "/Unidades", "i-unid", 7, GroupManagement, MasterOnly: true),
        new("Administradores", "/Administradores", "i-adm", 8, GroupManagement, MasterOnly: true),
    ];

    /// <summary>Só existe com a IA ligada (ADR-0004 §14): desligada, a tela responde 404 e o menu não a anuncia.</summary>
    private static readonly NavItem Ai = new("Rascunhos de IA", "/Questoes/Ia", "i-ai", 5, GroupContent);

    public static IReadOnlyList<NavItem> For(Func<string, bool> isInRole, bool aiEnabled = false)
    {
        var items = All.Where(i => !i.MasterOnly || isInRole(RoleNames.Master)).ToList();
        if (aiEnabled)
        {
            items.Insert(items.FindIndex(i => i.Path == "/Questoes") + 1, Ai);
        }

        return items;
    }
}
