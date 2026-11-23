using Helinho.Application.Abstractions;
using Helinho.Application.Ai;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Pages.Questoes.Ia;

/// <summary>Um pedido em lote: "gerando", falha, vazio ou os cartões de revisão (ADR-0004 §15).</summary>
public sealed class PedidoModel(GetAiDraftJob getJob, IClock clock, IOptions<AiOptions> ai) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid JobId { get; set; }

    public AiDraftJobView Job { get; private set; } = null!;

    public bool Demo => ai.Value.UsesFake;

    public bool InProgress => Job.Status is AiJobStatus.Pending or AiJobStatus.Processing;

    /// <summary>Recarrega sozinha por 2 minutos; depois disso fica só o link "Atualizar".</summary>
    public bool AutoRefresh => InProgress && clock.UtcNow - Job.CreatedAt < IaTexto.PollFor;

    public bool Slow => InProgress && !AutoRefresh;

    public Task<IActionResult> OnGetAsync(CancellationToken ct) => LoadAsync(ct);

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
