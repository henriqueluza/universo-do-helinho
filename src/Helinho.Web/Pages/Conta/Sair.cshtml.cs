using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Conta;

public sealed class SairModel(SignInManager<AppUser> signIn) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return LocalRedirect("/entrar");
    }
}
