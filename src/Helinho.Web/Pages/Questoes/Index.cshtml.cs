using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Questoes;

public sealed class IndexModel(IAppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public QuizType? Tipo { get; set; }

    [BindProperty(SupportsGet = true)]
    public AgeBand? Faixa { get; set; }

    [BindProperty(SupportsGet = true)]
    public QuestionStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public QuestionListResult Result { get; private set; } = null!;

    public bool HasFilters => Tipo is not null || Faixa is not null || Status is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Result = await ListQuestions.HandleAsync(db, new QuestionListQuery(Tipo, Faixa, Status, Pagina), cancellationToken);

    public static string StatusLabel(QuestionStatus status) => status == QuestionStatus.Approved ? "Aprovada" : "Rascunho";

    public string PageLink(int page) => Url.Page("/Questoes/Index", new { tipo = Tipo, faixa = Faixa, status = Status, pagina = page })!;
}
