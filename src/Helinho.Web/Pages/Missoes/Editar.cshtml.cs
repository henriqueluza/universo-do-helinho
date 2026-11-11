using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Missoes;

public sealed class EditarModel(IAppDbContext db) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Informe o título.")]
        [StringLength(120, ErrorMessage = "O título deve ter no máximo 120 caracteres.")]
        public string Title { get; set; } = "";

        [Range(1, 1000, ErrorMessage = "A meta deve ser um número positivo.")]
        public int Target { get; set; }

        [Range(1, 100000, ErrorMessage = "A recompensa deve ser um número positivo.")]
        public int RewardXp { get; set; }

        /// <summary>Vazio = missão genérica (sem tipo de quiz).</summary>
        public QuizType? QuizType { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var quest = await ManageDailyQuests.FindAsync(db, id, cancellationToken);
        if (quest is null)
        {
            return NotFound();
        }

        Input = new InputModel { Title = quest.Title, Target = quest.Target, RewardXp = quest.RewardXp, QuizType = quest.QuizType };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            if (!await ManageDailyQuests.UpdateAsync(db, id, Input.Title, Input.Target, Input.RewardXp, Input.QuizType, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        TempData["Aviso"] = "Missão salva.";
        return RedirectToPage("Index");
    }

    public string? Error(string field) => ModelState[$"Input.{field}"]?.Errors.FirstOrDefault()?.ErrorMessage;
}
