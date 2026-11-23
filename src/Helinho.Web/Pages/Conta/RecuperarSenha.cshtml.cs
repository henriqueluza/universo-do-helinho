using System.ComponentModel.DataAnnotations;
using Helinho.Application.Identidade;
using Helinho.Web.Composition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Helinho.Web.Pages.Conta;

/// <summary>Pedido de redefinição de senha (ADR-0003 §11.1): a resposta é a mesma exista a conta ou não. Quem envia o e-mail é a fila.</summary>
[EnableRateLimiting(RedefinicaoDeSenhaArea.RateLimitPolicy)]
public sealed class RecuperarSenhaModel(PedirRedefinicaoHandler pedidos) : PageModel
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

        await pedidos.HandleAsync(Input.Email, cancellationToken);
        Sent = true;
        return Page();
    }

    public sealed class RecoveryInput
    {
        [Required(ErrorMessage = "Informe o e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        public string Email { get; set; } = "";
    }
}
