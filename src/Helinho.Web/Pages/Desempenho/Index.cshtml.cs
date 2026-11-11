using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Desempenho;

public sealed class IndexModel(IAppDbContext db, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PerformanceListResult Result { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await StudentPerformanceQueries.ListAsync(db, clock, Faixa, Pagina, cancellationToken);
    }

    public static string BandLabel(AgeBand? band) => Alunos.IndexModel.BandLabel(band);

    public string PageLink(int page) => Url.Page("/Desempenho/Index", new { faixa = Faixa, pagina = page })!;
}
