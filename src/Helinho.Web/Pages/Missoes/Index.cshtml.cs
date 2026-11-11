using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Missoes;

public sealed class IndexModel(IAppDbContext db) : PageModel
{
    public IReadOnlyList<QuestRow> Quests { get; private set; } = [];

    [TempData]
    public string? Aviso { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Quests = await ManageDailyQuests.ListAsync(db, cancellationToken);

    public static string TypeLabel(QuizType? type) => type switch
    {
        null => "Genérica",
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Esportivo => "Esportivo",
        QuizType.Cultural => "Cultural",
        _ => "Diversidade",
    };
}
