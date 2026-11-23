using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Pages.Questoes.Ia;

/// <summary>Um pedido em lote: "gerando", falha, vazio ou os cartões de revisão (ADR-0004 §15).</summary>
public sealed class PedidoModel(GetAiDraftJob getJob, ReviewAiDrafts review, IClock clock, IOptions<AiOptions> ai) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid JobId { get; set; }

    public AiDraftJobView Job { get; private set; } = null!;

    public bool Demo => ai.Value.UsesFake;

    public bool InProgress => Job.Status is AiJobStatus.Pending or AiJobStatus.Processing;

    /// <summary>Recarrega sozinha por 2 minutos; depois disso fica só o link "Atualizar".</summary>
    public bool AutoRefresh => InProgress && clock.UtcNow - Job.CreatedAt < IaTexto.PollFor;

    public bool Slow => InProgress && !AutoRefresh;

    /// <summary>Resultado da última decisão (aprovar, descartar ou editar), mostrado uma vez depois do redirecionamento.</summary>
    [TempData]
    public string? Notice { get; set; }

    /// <summary>"ok" ou "warn": decide a cor do aviso.</summary>
    [TempData]
    public string? NoticeKind { get; set; }

    public Task<IActionResult> OnGetAsync(CancellationToken ct) => LoadAsync(ct);

    public async Task<IActionResult> OnPostAprovarAsync(Guid id, CancellationToken ct) =>
        Decided(await review.ApproveAsync(id, ct), "Sugestão aprovada. A questão já pode chegar ao jogo.");

    public async Task<IActionResult> OnPostDescartarAsync(Guid id, CancellationToken ct) =>
        Decided(await review.DiscardAsync(id, ct), "Sugestão descartada.");

    /// <summary>Traduz o resultado do caso de uso. Desligada vira 404; as demais voltam ao pedido com uma mensagem.</summary>
    private IActionResult Decided(AiDraftDecision decision, string done)
    {
        switch (decision.Status)
        {
            case AiDraftDecisionStatus.Disabled or AiDraftDecisionStatus.NotFound:
                return NotFound();
            case AiDraftDecisionStatus.Forbidden:
                return Forbid();
            case AiDraftDecisionStatus.Done:
                (Notice, NoticeKind) = (done, "ok");
                break;
            case AiDraftDecisionStatus.Duplicate:
                (Notice, NoticeKind) = ("Já existe uma questão com este enunciado neste tipo e faixa. Edite a sugestão ou descarte.", "warn");
                break;
            default:
                (Notice, NoticeKind) = ("Esta sugestão já foi revisada. A lista foi atualizada.", "warn");
                break;
        }

        return RedirectToPage(new { jobId = JobId });
    }

    /// <summary>Lê o pedido; o resultado é a página, 404 (desligada ou inexistente) ou 403.</summary>
    private async Task<IActionResult> LoadAsync(CancellationToken ct)
    {
        var result = await getJob.HandleAsync(JobId, ct);
        switch (result.Status)
        {
            case GetAiDraftJobStatus.Found:
                Job = result.Job!;
                return Page();
            case GetAiDraftJobStatus.Forbidden:
                return Forbid();
            default:
                return NotFound();
        }
    }

    /// <summary>"Pedir de novo": o formulário volta com os mesmos dados do pedido.</summary>
    public string AgainUrl()
    {
        var query = new Dictionary<string, string?>
        {
            ["tipo"] = Job.QuizType.ToString(),
            ["faixa"] = Job.AgeBand.ToString(),
            ["qtd"] = Job.RequestedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["assunto"] = Job.Subject,
        };
        return QueryHelpers.AddQueryString("/Questoes/Ia", query);
    }
}
