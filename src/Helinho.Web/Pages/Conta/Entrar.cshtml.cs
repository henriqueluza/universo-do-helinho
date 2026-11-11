using System.ComponentModel.DataAnnotations;
using Helinho.Domain;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Conta;

public sealed class EntrarModel(SignInManager<AppUser> signIn, UserManager<AppUser> users) : PageModel
{
    // Mesma mensagem para usuário inexistente, senha errada e papel sem acesso (não revela contas).
    private const string InvalidMessage = "Usuário ou senha inválidos.";

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByNameAsync(Input.UserName) ?? await users.FindByEmailAsync(Input.UserName);
        if (user is null || !await HasPanelRoleAsync(user))
        {
            ModelState.AddModelError(string.Empty, InvalidMessage);
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Acesso bloqueado por tentativas excessivas. Tente novamente mais tarde.");
            return Page();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, InvalidMessage);
            return Page();
        }

        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : LocalRedirect("/");
    }

    private async Task<bool> HasPanelRoleAsync(AppUser user) =>
        await users.IsInRoleAsync(user, RoleNames.Master) || await users.IsInRoleAsync(user, RoleNames.Admin);

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Informe o usuário ou e-mail.")]
        public string UserName { get; set; } = "";

        [Required(ErrorMessage = "Informe a senha.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
