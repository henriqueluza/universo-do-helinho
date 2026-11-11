using Helinho.Application.Abstractions;
using Helinho.Application.Painel;
using Microsoft.AspNetCore.Mvc;

namespace Helinho.Web.Pages.Alunos;

public sealed class NovoModel(IAppDbContext db, ICurrentUser user, IClock clock, IStudentAccounts accounts) : StudentFormPage
{
    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        UnitName = await SaveStudent.UnitNameAsync(db, user, cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await SaveStudent.CreateAsync(db, user, clock, accounts, Input.ToForm(), cancellationToken);
        if (result.Succeeded)
        {
            TempData["Aviso"] = "Aluno cadastrado.";
            return RedirectToPage("/Alunos/Index");
        }

        Errors = result.Errors;
        UnitName = await SaveStudent.UnitNameAsync(db, user, cancellationToken);
        return Page();
    }
}
