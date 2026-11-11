using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Eventos;

public sealed class IndexModel(IAppDbContext db, IClock clock) : PageModel
{
    public IReadOnlyList<LiveEventRow> Events { get; private set; } = [];

    [BindProperty]
    public EventInput Input { get; set; } = new();

    [TempData]
    public string? Saved { get; set; }

    public bool ShowForm { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var endsAt = BrasiliaTime.ToUtc(Input.EndsAt!.Value);
                await CreateLiveEvent.HandleAsync(db, clock, Input.Title, Input.Subtitle, Input.QuizType!.Value, endsAt, ct);
                Saved = "Evento criado.";
                return RedirectToPage();
            }
            catch (DomainException e)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(EventInput.EndsAt)}", e.Message);
            }
        }

        ShowForm = true;
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct) => Events = await ListLiveEvents.HandleAsync(db, clock, ct);

    public static string QuizLabel(QuizType t) => t switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Cultural => "Cultural",
        QuizType.Esportivo => "Esportivo",
        _ => "Diversidade",
    };

    public sealed class EventInput
    {
        [Required(ErrorMessage = "Informe o título.")]
        [StringLength(CreateLiveEvent.TitleMax, ErrorMessage = "O título deve ter até 120 caracteres.")]
        public string? Title { get; set; }

        [StringLength(CreateLiveEvent.SubtitleMax, ErrorMessage = "O subtítulo deve ter até 200 caracteres.")]
        public string? Subtitle { get; set; }

        [Required(ErrorMessage = "Escolha o tipo de quiz.")]
        public QuizType? QuizType { get; set; }

        [Required(ErrorMessage = "Informe a data e a hora de término.")]
        public DateTime? EndsAt { get; set; }
    }
}
