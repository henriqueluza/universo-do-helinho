using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Pages.Questoes.Ia;

/// <summary>Pedir um lote de rascunhos (ADR-0004 §15). Com a IA desligada a página não existe: 404.</summary>
public sealed class IndexModel(RequestAiDrafts request, IAppDbContext db, ICurrentUser user, IOptions<AiOptions> ai) : PageModel
{
    public const int SubjectMax = 120;

    [BindProperty]
    public PedidoInput Input { get; set; } = new();

    public bool Demo => ai.Value.UsesFake;

    public string? QuotaMessage { get; private set; }

    public IReadOnlyList<RecentRequest> Recent { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(QuizType? tipo, AgeBand? faixa, int? qtd, string? assunto, CancellationToken ct)
    {
        if (!ai.Value.IsEnabled)
        {
            return NotFound();
        }

        // "Pedir de novo" volta com os mesmos dados; valor fora do enum é ignorado.
        Input = new PedidoInput
        {
            QuizType = tipo is { } t && Enum.IsDefined(t) ? t : null,
            AgeBand = faixa is { } f && Enum.IsDefined(f) ? f : null,
            Count = qtd is { } q && IaTexto.Quantities.Contains(q) ? q : IaTexto.Quantities[0],
            Subject = assunto is { Length: <= SubjectMax } ? assunto : null,
        };
        await LoadRecentAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ai.Value.IsEnabled)
        {
            return NotFound();
        }

        if (!IaTexto.Quantities.Contains(Input.Count) && ModelState["Input.Count"]?.Errors.Count is null or 0)
        {
            ModelState.AddModelError("Input.Count", "Escolha 5, 10 ou 20 questões.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var i = Input;
                var result = await request.HandleAsync(i.QuizType!.Value, i.AgeBand!.Value, i.Count, i.Subject, ct);
                switch (result.Status)
                {
                    case RequestAiDraftsStatus.Accepted or RequestAiDraftsStatus.AlreadyRequested:
                        return SeeOther(Url.Page("Pedido", new { jobId = result.JobId })!);
                    case RequestAiDraftsStatus.Disabled:
                        return NotFound();
                    case RequestAiDraftsStatus.Forbidden:
                        return Forbid();
                    default:
                        QuotaMessage = IaTexto.Quota(result.Quota!, i.Count);
                        break;
                }
            }
            catch (DomainException e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        await LoadRecentAsync(ct);
        return Page();
    }

    /// <summary>303, como pede o ADR: depois do POST o navegador troca para GET.</summary>
    private EmptyResult SeeOther(string url)
    {
        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = url;
        return new EmptyResult();
    }

    /// <summary>Os últimos pedidos da própria pessoa: é por aqui que as sugestões não revisadas são reencontradas.</summary>
    private async Task LoadRecentAsync(CancellationToken ct)
    {
        if (user.UserId is not { } userId)
        {
            return;
        }

        var jobs = await db.AiGenerationJobs.AsNoTracking()
            .Where(j => j.Kind == AiJobKind.Batch && j.RequestedByUserId == userId)
            .OrderByDescending(j => j.CreatedAt).Take(5).ToListAsync(ct);
        var ids = jobs.Select(j => j.Id).ToList();
        var pending = await db.AiQuestionSuggestions.AsNoTracking()
            .Where(s => ids.Contains(s.JobId) && s.Status == AiSuggestionStatus.Pending)
            .GroupBy(s => s.JobId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        Recent = [.. jobs.Select(j => new RecentRequest(j.Id, j.QuizType, j.AgeBand, j.RequestedCount, j.Status, j.CreatedAt, pending.GetValueOrDefault(j.Id)))];
    }

    public sealed record RecentRequest(Guid Id, QuizType QuizType, AgeBand AgeBand, int Count, AiJobStatus Status, DateTimeOffset CreatedAt, int PendingReview);

    public sealed class PedidoInput
    {
        [Required(ErrorMessage = "Escolha o tipo de quiz.")]
        public QuizType? QuizType { get; set; }

        [Required(ErrorMessage = "Escolha a faixa etária.")]
        public AgeBand? AgeBand { get; set; }

        [Range(5, 20, ErrorMessage = "Escolha 5, 10 ou 20 questões.")]
        public int Count { get; set; } = 5;

        [StringLength(SubjectMax, ErrorMessage = "O assunto deve ter até 120 caracteres.")]
        public string? Subject { get; set; }
    }
}
