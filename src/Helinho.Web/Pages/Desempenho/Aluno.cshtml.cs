using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Desempenho;

public sealed class AlunoModel(IAppDbContext db, IClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public StudentPerformance Student { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Aluno de outra unidade não passa pelo filtro global: 404, igual a um id que não existe.
        var found = await StudentPerformanceQueries.GetAsync(db, clock, Id, cancellationToken);
        if (found is null)
        {
            return NotFound();
        }

        Student = found;
        return Page();
    }

    public static string TypeLabel(Helinho.Domain.QuizType type) => type switch
    {
        Helinho.Domain.QuizType.Ambiental => "Ambiental",
        Helinho.Domain.QuizType.Conhecimento => "Conhecimento",
        Helinho.Domain.QuizType.Esportivo => "Esportivo",
        Helinho.Domain.QuizType.Cultural => "Cultural",
        _ => "Diversidade",
    };
}
