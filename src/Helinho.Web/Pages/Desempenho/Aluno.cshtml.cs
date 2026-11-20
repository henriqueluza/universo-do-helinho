using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Application.Painel;
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

    /// <summary>Arte do selo pelo título, ou null (a tela mostra o ícone de estrela). Largura sempre 96.</summary>
    public static (string Src, int Height)? BadgeImage(string title) =>
        BadgeArt.TryGetValue(title, out var art) ? ($"/img/{art.File}.webp", art.Height) : null;

    /// <summary>Iniciais do avatar: primeiro caractere visível (grafema) do primeiro e do último nome.</summary>
    public static string Initials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => StringInfo.GetNextTextElement(parts[0]).ToUpperInvariant(),
            _ => string.Concat(StringInfo.GetNextTextElement(parts[0]), StringInfo.GetNextTextElement(parts[^1])).ToUpperInvariant(),
        };
    }

    /// <summary>Preenchimento da barra de nível (0 a 100); o valor também aparece em texto na ficha.</summary>
    public static int XpPercent(int xp, int nextLevelXp) => nextLevelXp <= 0 ? 0 : Math.Clamp(100 * xp / nextLevelXp, 0, 100);

    /// <summary>Aproveitamento de um tipo de quiz (0 a 100); a tela mostra o número em texto ao lado da barra.</summary>
    public static int CorrectPercent(int answered, int correct) => answered <= 0 ? 0 : Math.Clamp(100 * correct / answered, 0, 100);

    /// <summary>Data de desbloqueio no fuso de Brasília (as datas são gravadas em UTC).</summary>
    public static string UnlockedOn(DateTimeOffset unlockedAt) =>
        BrasiliaTime.ToLocal(unlockedAt).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
