using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Application.Questions;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Questoes;

public sealed class EditarModel(IAppDbContext db, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null)
        {
            return Page();
        }

        var q = await GetQuestion.HandleAsync(db, Id.Value, ct);
        if (q is null)
        {
            return NotFound();
        }

        Input = new QuestionInput
        {
            QuizType = q.QuizType,
            AgeBand = q.AgeBand,
            Statement = q.Statement,
            Explanation = q.Explanation,
            OptionA = q.Options[0],
            OptionB = q.Options[1],
            OptionC = q.Options[2],
            OptionD = q.Options[3],
            Correct = q.CorrectIndex,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var i = Input;
                var saved = await SaveQuestion.HandleAsync(db, Id, i.QuizType!.Value, i.AgeBand!.Value, i.Statement, i.Explanation,
                    [i.OptionA, i.OptionB, i.OptionC, i.OptionD], i.Correct, ct, clock.UtcNow);
                if (saved is null)
                {
                    return NotFound();
                }

                TempData["Saved"] = Id is null ? "Questão criada como rascunho." : "Questão salva e devolvida a rascunho.";
                return RedirectToPage("Index");
            }
            catch (DomainException e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        return Page();
    }

    public sealed class QuestionInput
    {
        [Required(ErrorMessage = "Escolha o tipo de quiz.")]
        public QuizType? QuizType { get; set; }

        [Required(ErrorMessage = "Escolha a faixa etária.")]
        public AgeBand? AgeBand { get; set; }

        [Required(ErrorMessage = "Informe o enunciado.")]
        [StringLength(SaveQuestion.StatementMax, ErrorMessage = "O enunciado deve ter até 500 caracteres.")]
        public string? Statement { get; set; }

        [Required(ErrorMessage = "Informe a alternativa A.")]
        [StringLength(SaveQuestion.OptionMax, ErrorMessage = "A alternativa A deve ter até 300 caracteres.")]
        public string? OptionA { get; set; }

        [Required(ErrorMessage = "Informe a alternativa B.")]
        [StringLength(SaveQuestion.OptionMax, ErrorMessage = "A alternativa B deve ter até 300 caracteres.")]
        public string? OptionB { get; set; }

        [Required(ErrorMessage = "Informe a alternativa C.")]
        [StringLength(SaveQuestion.OptionMax, ErrorMessage = "A alternativa C deve ter até 300 caracteres.")]
        public string? OptionC { get; set; }

        [Required(ErrorMessage = "Informe a alternativa D.")]
        [StringLength(SaveQuestion.OptionMax, ErrorMessage = "A alternativa D deve ter até 300 caracteres.")]
        public string? OptionD { get; set; }

        [Required(ErrorMessage = "Marque a alternativa correta.")]
        [Range(0, 3, ErrorMessage = "Marque a alternativa correta.")]
        public int? Correct { get; set; }

        [Required(ErrorMessage = "Informe a explicação.")]
        [StringLength(SaveQuestion.ExplanationMax, ErrorMessage = "A explicação deve ter até 1000 caracteres.")]
        public string? Explanation { get; set; }
    }
}
