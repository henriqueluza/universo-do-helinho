using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Questoes;

public sealed class IndexModel(IAppDbContext db) : PageModel
{
    public IReadOnlyList<QuestionRow> Questions { get; private set; } = [];

    [TempData]
    public string? Saved { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => Questions = await ListQuestions.HandleAsync(db, ct);

    public async Task<IActionResult> OnPostAprovarAsync(Guid id, CancellationToken ct)
    {
        if (!await ApproveQuestion.HandleAsync(db, id, ct))
        {
            return NotFound();
        }

        Saved = "Questão aprovada.";
        return RedirectToPage();
    }

    public static string QuizLabel(QuizType t) => t switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Cultural => "Cultural",
        QuizType.Esportivo => "Esportivo",
        _ => "Diversidade",
    };

    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6–7 anos",
        AgeBand.Age8To10 => "8–10 anos",
        AgeBand.Age11To12 => "11–12 anos",
        _ => "13–14 anos",
    };
}
