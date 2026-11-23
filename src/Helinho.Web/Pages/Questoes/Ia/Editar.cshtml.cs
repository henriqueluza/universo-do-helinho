using System.ComponentModel.DataAnnotations;
using Helinho.Application.Ai;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Questoes.Ia;

/// <summary>
/// Edita uma sugestão antes de salvá-la (ADR-0004 §9.3). Abrir o formulário não grava nada: só o POST cria a questão,
/// em rascunho e com origem IA. Cancelar deixa a sugestão pendente.
/// </summary>
public sealed class EditarModel(GetAiDraftJob getJob, ReviewAiDrafts review) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid JobId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public EdicaoInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var draft = await FindPendingAsync(ct);
        if (draft.Result is not null)
        {
            return draft.Result;
        }

        var d = draft.Draft!;
        Input = new EdicaoInput
        {
            QuizType = draft.Job!.QuizType,
            AgeBand = draft.Job.AgeBand,
            Statement = d.Statement,
            OptionA = d.Options.ElementAtOrDefault(0),
            OptionB = d.Options.ElementAtOrDefault(1),
            OptionC = d.Options.ElementAtOrDefault(2),
            OptionD = d.Options.ElementAtOrDefault(3),
            Correct = d.CorrectIndex,
            Explanation = d.Explanation,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // A sugestão tem de ser do pedido da URL e ainda estar pendente, como ao abrir o formulário.
        if ((await FindPendingAsync(ct)).Result is { } stop)
        {
            return stop;
        }

        if (ModelState.IsValid)
        {
            try
            {
                var i = Input;
                var decision = await review.SaveEditedAsync(Id, i.QuizType!.Value, i.AgeBand!.Value, i.Statement, i.Explanation,
                    [i.OptionA, i.OptionB, i.OptionC, i.OptionD], i.Correct, ct);
                switch (decision.Status)
                {
                    case AiDraftDecisionStatus.Disabled or AiDraftDecisionStatus.NotFound:
                        return NotFound();
                    case AiDraftDecisionStatus.Forbidden:
                        return Forbid();
                    case AiDraftDecisionStatus.Done:
                        TempData["Notice"] = "Sugestão editada e salva como rascunho. Ela precisa ser aprovada em Questões para chegar ao jogo.";
                        TempData["NoticeKind"] = "ok";
                        break;
                    default:
                        TempData["Notice"] = "Esta sugestão já foi revisada. A lista foi atualizada.";
                        TempData["NoticeKind"] = "warn";
                        break;
                }

                return RedirectToPage("Pedido", new { jobId = JobId });
            }
            catch (DomainException e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        return Page();
    }

    private async Task<(IActionResult? Result, AiDraftJobView? Job, AiDraftView? Draft)> FindPendingAsync(CancellationToken ct)
    {
        var result = await getJob.HandleAsync(JobId, ct);
        if (result.Status == GetAiDraftJobStatus.Forbidden)
        {
            return (Forbid(), null, null);
        }

        var draft = result.Job?.Drafts.FirstOrDefault(d => d.Id == Id);
        if (draft is null)
        {
            return (NotFound(), null, null);
        }

        if (draft.Status != AiSuggestionStatus.Pending)
        {
            TempData["Notice"] = "Esta sugestão já foi revisada. A lista foi atualizada.";
            TempData["NoticeKind"] = "warn";
            return (RedirectToPage("Pedido", new { jobId = JobId }), null, null);
        }

        return (null, result.Job, draft);
    }

    public sealed class EdicaoInput
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
