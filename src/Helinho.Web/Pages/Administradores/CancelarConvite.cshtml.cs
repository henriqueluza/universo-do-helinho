using Helinho.Application.Administradores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Administradores;

/// <summary>Confirmação em página própria (sem script) antes de apagar a conta que nunca teve senha (ADR-0003 §11.2).</summary>
public sealed class CancelarConviteModel(ConviteAbertoHandler convites) : PageModel
{
    public string UserName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await convites.AbertoAsync(id, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        UserName = account.UserName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var status = await convites.CancelarAsync(id, cancellationToken);
        if (status == ConviteStatus.Done)
        {
            TempData[nameof(IndexModel.Aviso)] = "Convite cancelado e conta apagada. A unidade está livre para um novo administrador.";
        }
        else
        {
            TempData[nameof(IndexModel.Erro)] = IndexModel.Recusa(status);
        }

        return RedirectToPage("Index");
    }
}
