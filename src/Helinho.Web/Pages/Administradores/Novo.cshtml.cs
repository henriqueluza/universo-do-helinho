using System.ComponentModel.DataAnnotations;
using Helinho.Application.Administradores;
using Helinho.Domain.Entities;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Administradores;

public sealed class NovoModel(CriarAdministradorHandler criar) : PageModel
{
    [BindProperty]
    public AdminInput Input { get; set; } = new();

    public IReadOnlyList<Unit> Units { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadUnitsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadUnitsAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await criar.HandleAsync(new NewAdminAccount(Input.UserName, Input.Email, Input.UnitId!.Value), cancellationToken);
        switch (result.Status)
        {
            case CriarAdministradorStatus.Created:
                return Redirect("/Administradores");
            case CriarAdministradorStatus.InvalidUnit:
                ModelState.AddModelError("Input.UnitId", "Selecione uma unidade válida.");
                break;
            case CriarAdministradorStatus.UnitHasActiveAdmin:
                ModelState.AddModelError("Input.UnitId", "Esta unidade já tem um administrador ativo.");
                break;
            default:
                foreach (var code in result.ErrorCodes)
                {
                    var (field, message) = IdentityMensagens.Traduzir(code);
                    ModelState.AddModelError(field, message);
                }

                break;
        }

        return Page();
    }

    private async Task LoadUnitsAsync(CancellationToken cancellationToken) =>
        Units = await criar.ListUnitsAsync(cancellationToken);

    public sealed class AdminInput
    {
        [Required(ErrorMessage = "Informe o nome de usuário.")]
        [StringLength(60, ErrorMessage = "O nome de usuário pode ter no máximo 60 caracteres.")]
        public string UserName { get; set; } = "";

        [Required(ErrorMessage = "Informe o e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        [StringLength(200, ErrorMessage = "O e-mail pode ter no máximo 200 caracteres.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Selecione a unidade.")]
        public Guid? UnitId { get; set; }
    }
}
