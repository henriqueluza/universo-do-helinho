using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Application.Questions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Pages.Questoes;

public sealed class EditarModel(IAppDbContext db, IClock clock, SuggestSingleDraft suggest, IOptions<AiOptions> ai) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    /// <summary>Pedido avulso que gerou o texto sugerido (campo oculto). Com ele, ao salvar, a questão nasce com origem IA.</summary>
    [BindProperty]
    public Guid? AiJobId { get; set; }

    /// <summary>Confirmação de que a sugestão pode trocar o que a pessoa já escreveu (ADR-0004 §15).</summary>
    [BindProperty]
    public bool Substituir { get; set; }

    /// <summary>O "Sugerir" só existe ao criar e com a IA ligada (ADR-0004 §14).</summary>
    public bool AiEnabled => Id is null && ai.Value.IsEnabled;

    public bool AiDemo => ai.Value.UsesFake;

    public AiState AiState { get; private set; }

    public string? AiMessage { get; private set; }

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

    /// <summary>
    /// "Sugerir" (ADR-0004 §15): pede uma sugestão e devolve o formulário preenchido. Nunca salva. Se a pessoa já escreveu
    /// algo, não troca nada sem a confirmação; em falha, o que ela escreveu continua intacto.
    /// </summary>
    public async Task<IActionResult> OnPostSugerirAsync(CancellationToken ct)
    {
        if (!AiEnabled)
        {
            return NotFound();
        }

        ModelState.Clear();
        if (Input.QuizType is null || Input.AgeBand is null)
        {
            if (Input.QuizType is null)
            {
                ModelState.AddModelError("Input.QuizType", "Escolha o tipo de quiz para pedir a sugestão.");
            }

            if (Input.AgeBand is null)
            {
                ModelState.AddModelError("Input.AgeBand", "Escolha a faixa etária para pedir a sugestão.");
            }

            return Page();
        }

        if (HasText() && !Substituir)
        {
            AiState = AiState.Confirm;
            return Page();
        }

        var result = await suggest.HandleAsync(Input.QuizType.Value, Input.AgeBand.Value, ct);
        switch (result)
        {
            case { Status: SuggestSingleDraftStatus.Disabled }:
                return NotFound();
            case { Status: SuggestSingleDraftStatus.Forbidden }:
                return Forbid();
            case { Status: SuggestSingleDraftStatus.Suggested, Draft: { } draft, JobId: { } jobId }:
                Input.Statement = draft.Statement;
                Input.Explanation = draft.Explanation;
                Input.OptionA = draft.Options[0];
                Input.OptionB = draft.Options[1];
                Input.OptionC = draft.Options[2];
                Input.OptionD = draft.Options[3];
                Input.Correct = draft.CorrectIndex;
                AiJobId = jobId;
                AiState = AiState.Filled;
                break;
            default:
                AiState = AiState.Failed;
                AiMessage = Message(result);
                break;
        }

        return Page();
    }

    private bool HasText() =>
        new[] { Input.Statement, Input.Explanation, Input.OptionA, Input.OptionB, Input.OptionC, Input.OptionD }.Any(t => !string.IsNullOrWhiteSpace(t));

    /// <summary>Mensagem para a pessoa, por código fechado (ADR-0004 §15), sem jargão e sem detalhe do provedor.</summary>
    public static string Message(SuggestSingleDraftResult result) => result.Status switch
    {
        SuggestSingleDraftStatus.Rejected => "A sugestão veio com problema e foi descartada. Tente de novo.",
        SuggestSingleDraftStatus.QuotaExceeded when result.Quota?.Global == true => "O limite diário de sugestões da IA foi atingido. Tente mais tarde.",
        SuggestSingleDraftStatus.QuotaExceeded => "Você chegou ao limite de sugestões da IA por hoje. Tente mais tarde.",
        _ => result.FailureReason switch
        {
            AiFailureCodes.Timeout => "A IA demorou para responder. Tente de novo.",
            "ai_unavailable" or "ai_rate_limited" => "A IA está indisponível agora. Tente mais tarde.",
            "ai_refused" => "A IA não gerou uma sugestão desta vez. Tente de novo.",
            "ai_invalid_output" => "A sugestão veio com problema e foi descartada. Tente de novo.",
            "ai_misconfigured" => "A IA não está configurada. Avise o responsável técnico.",
            _ => "Não foi possível pedir a sugestão agora. Tente mais tarde.",
        },
    };

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

/// <summary>O que o "Sugerir" deixou na página: nada, formulário preenchido, pedido de confirmação ou falha.</summary>
public enum AiState
{
    None,
    Filled,
    Confirm,
    Failed,
}
