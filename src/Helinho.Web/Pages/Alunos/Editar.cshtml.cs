using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Pages.Alunos;

public sealed class EditarModel(IAppDbContext db, ICurrentUser user, IClock clock) : StudentFormPage
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        // Aluno de outra unidade não passa pelo filtro global: 404, igual a um id que não existe.
        var form = await SaveStudent.LoadAsync(db, Id, cancellationToken);
        if (form is null)
        {
            return NotFound();
        }

        Input = StudentInput.From(form);
        UnitName = await SaveStudent.UnitNameAsync(db, user, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await SaveStudent.UpdateAsync(db, clock, Id, Input.ToForm(), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData["Aviso"] = "Cadastro atualizado.";
            return RedirectToPage("/Alunos/Index");
        }

        Errors = result.Errors;
        UnitName = await SaveStudent.UnitNameAsync(db, user, cancellationToken);
        return Page();
    }
}
