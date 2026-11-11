using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Alunos;

public sealed class IndexModel(IAppDbContext db, ICurrentUser user, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Turma { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Unidade { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public bool IsMaster => user.IsInRole(RoleNames.Master);

    public StudentListResult Result { get; private set; } = null!;

    public bool HasFilters => Faixa is not null || !string.IsNullOrWhiteSpace(Turma) || Unidade is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await ListStudents.HandleAsync(db, user, clock, new StudentListQuery(Unidade, Turma, Faixa, Pagina), cancellationToken);
        Pagina = Result.Page;
    }

    public AgeBand BandOf(DateOnly birthDate) =>
        AgeBands.FromBirthDate(birthDate, DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime));

    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.Age6To7 => "6–7",
        AgeBand.Age8To10 => "8–10",
        AgeBand.Age11To12 => "11–12",
        _ => "13–14",
    };

    public string PageLink(int page) =>
        Url.Page("/Alunos/Index", new { faixa = Faixa, turma = Turma, unidade = IsMaster ? Unidade : null, pagina = page })!;
}
