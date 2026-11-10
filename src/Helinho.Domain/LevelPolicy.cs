namespace Helinho.Domain;

/// <summary>
/// PROPOSTA (P13): fórmula simples de XP → nível, ainda não aceita pela contratante.
/// O nível n começa em 50·n·(n−1) XP (1→0, 2→100, 3→300, 4→600…).
/// </summary>
public static class LevelPolicy
{
    public static int LevelFor(int xp)
    {
        var level = 1;
        while (xp >= XpForLevel(level + 1))
        {
            level++;
        }

        return level;
    }

    /// <summary>Limiar de XP do próximo nível (`next_level_xp`), não o que falta.</summary>
    public static int NextLevelXp(int xp) => XpForLevel(LevelFor(xp) + 1);

    public static string LevelName(int level) => level switch
    {
        <= 2 => "Novato",
        <= 5 => "Explorador",
        <= 9 => "Aventureiro",
        _ => "Mestre",
    };

    private static int XpForLevel(int level) => 50 * level * (level - 1);
}
