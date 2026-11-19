using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Desempenho;

public sealed class AlunoModel(IAppDbContext db, IClock clock) : PageModel
{
    // PROPOSTA: o backend devolve só o título do selo; as artes entregues são de 4 selos conquistáveis. Casa pelo título.
    private static readonly Dictionary<string, (string File, int Height)> BadgeArt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Amigo da natureza"] = ("selo-amigo-da-natureza", 101),
        ["Curioso do mês"] = ("selo-curioso-do-mes", 101),
        ["Leitor da Lia"] = ("selo-leitor-da-lia", 101),
        ["Cem perguntas"] = ("selo-cem-perguntas", 109),
    };

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public StudentPerformance Student { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Aluno de outra unidade não passa pelo filtro global: 404, igual a um id que não existe.
        var found = await StudentPerformanceQueries.GetAsync(db, clock, Id, cancellationToken);
        if (found is null)
        {
            return NotFound();
        }

        Student = found;
        return Page();
    }

    public static string TypeLabel(QuizType type) => type switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Esportivo => "Esportivo",
        QuizType.Cultural => "Cultural",
        _ => "Diversidade",
    };

    /// <summary>Personagem do tema: o tipo de quiz aparece sempre com nome e personagem, nunca só pela cor.</summary>
    public static string CharacterName(QuizType type) => type switch
    {
        QuizType.Ambiental => "Flora",
        QuizType.Conhecimento => "Byte",
        QuizType.Esportivo => "Max",
        QuizType.Cultural => "Luna",
        _ => "Lia",
    };

    /// <summary>Classe de cor do tema (<c>.amb</c>, <c>.con</c>…) e arquivo da carinha do personagem.</summary>
    public static (string Css, string Image) ThemeAssets(QuizType type) => type switch
    {
        QuizType.Ambiental => ("amb", "cara-flora"),
        QuizType.Conhecimento => ("con", "cara-byte"),
        QuizType.Esportivo => ("esp", "cara-max"),
        QuizType.Cultural => ("cul", "cara-luna"),
        _ => ("div", "cara-lia"),
    };

    /// <summary>Arte do selo pelo título, ou null (a tela mostra o ícone de estrela). Largura sempre 96.</summary>
    public static (string Src, int Height)? BadgeImage(string title) =>
        BadgeArt.TryGetValue(title, out var art) ? ($"/img/{art.File}.webp", art.Height) : null;

    public static string Initials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        return (parts.Length == 1 ? parts[0][..1] : $"{parts[0][..1]}{parts[^1][..1]}").ToUpperInvariant();
    }

    public static int XpPercent(int xp, int nextLevelXp) => nextLevelXp <= 0 ? 0 : Math.Clamp(100 * xp / nextLevelXp, 0, 100);
}
