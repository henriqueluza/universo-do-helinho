using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Conta;

/// <summary>Recuperação de senha sem envio real: a resposta é idêntica exista a conta ou não (não revela contas).</summary>
public sealed class RecuperarSenhaModel(UserManager<AppUser> users, IEmailSender email) : PageModel
{
    [BindProperty]
    public RecoveryInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email);
        if (user?.Email is { } address && await HasPanelRoleAsync(user))
        {
            // Sem token nem link: a redefinição real é outra Issue. O corpo não carrega segredo.
            await email.SendAsync(address, "Recuperação de senha",
                "Recebemos um pedido de recuperação de senha para esta conta.", cancellationToken);
        }

        Sent = true;
        return Page();
    }

    private async Task<bool> HasPanelRoleAsync(AppUser user) =>
        await users.IsInRoleAsync(user, RoleNames.Master) || await users.IsInRoleAsync(user, RoleNames.Admin);

    public sealed class RecoveryInput
    {
        [Required(ErrorMessage = "Informe o e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        public string Email { get; set; } = "";
    }
}
