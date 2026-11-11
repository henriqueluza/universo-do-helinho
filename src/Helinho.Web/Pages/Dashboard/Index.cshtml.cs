using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Dashboard;

public sealed class IndexModel(IAppDbContext db, ICurrentUser user, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Dias { get; set; } = GetDashboard.DefaultDays;

    [BindProperty(SupportsGet = true)]
    public Guid? Unidade { get; set; }

    public bool IsMaster => user.IsInRole(RoleNames.Master);

    public DashboardResult Result { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Dias = GetDashboard.NormalizeDays(Dias);
        Result = await GetDashboard.HandleAsync(db, user, clock, new DashboardQuery(Faixa, Dias, Unidade), cancellationToken);
    }

    public bool HasFilters => Faixa is not null || Unidade is not null || Dias != GetDashboard.DefaultDays;

    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6–7",
        AgeBand.Age8To10 => "8–10",
        AgeBand.Age11To12 => "11–12",
        _ => "13–14",
    };

    public static string TypeLabel(QuizType type) => type switch
    {
        QuizType.Ambiental => "Ambiental",
        QuizType.Conhecimento => "Conhecimento",
        QuizType.Esportivo => "Esportivo",
        QuizType.Cultural => "Cultural",
        _ => "Diversidade",
    };

    public static string TypeClass(QuizType type) => "quiz-" + type.ToString().ToLowerInvariant();

    public string Link(AgeBand? faixa, int? dias = null, Guid? unidade = null) =>
        Url.Page("/Dashboard/Index", new { faixa, dias = dias ?? Dias, unidade = unidade ?? Unidade })!;
}
