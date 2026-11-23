using System.ComponentModel.DataAnnotations;
using Helinho.Application.Identidade;
using Helinho.Web.Composition;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Helinho.Web.Pages.Conta;

/// <summary>
/// Destino do link de acesso do e-mail (ADR-0003 §10.3 e §11.3): convite e redefinição na mesma página.
/// O <c>GET</c> só confere o link; quem o consome é o <c>POST</c>. Não faz login automático.
/// </summary>
[EnableRateLimiting(IdentidadeArea.DefinirSenhaRateLimitPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DefinirSenhaModel(DefinirSenhaHandler handler) : PageModel
{
    public enum Etapa { Formulario, LinkInvalido, Definida }

    [BindProperty(SupportsGet = true)]
    public string? Usuario { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public Etapa Atual { get; private set; }

    /// <summary>O token está na URL: o endereço desta página não pode seguir no <c>Referer</c> de nenhum pedido.</summary>
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context) =>
        Response.Headers["Referrer-Policy"] = "no-referrer";

    public async Task OnGetAsync(bool definida, CancellationToken cancellationToken) =>
        Atual = definida ? Etapa.Definida : await EtapaDoLinkAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Atual = await EtapaDoLinkAsync(cancellationToken);
            return Page();
        }

        var result = await handler.HandleAsync(Usuario, Token, Input.Password, cancellationToken);
        if (result.Status == DefinirSenhaStatus.Definida)
        {
            return RedirectToPage(new { definida = true });
        }

        foreach (var (campo, mensagem) in result.ErrorCodes.Select(IdentityMensagens.Traduzir))
        {
            ModelState.AddModelError(campo, mensagem);
        }

        Atual = result.Status == DefinirSenhaStatus.SenhaRecusada ? Etapa.Formulario : Etapa.LinkInvalido;
        return Page();
    }

    private async Task<Etapa> EtapaDoLinkAsync(CancellationToken cancellationToken) =>
        await handler.LinkValidoAsync(Usuario, Token, cancellationToken) ? Etapa.Formulario : Etapa.LinkInvalido;

    public sealed class PasswordInput
    {
        [Required(ErrorMessage = "Informe a nova senha.")]
        [StringLength(128, ErrorMessage = "A senha pode ter até 128 caracteres.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Repita a nova senha.")]
        [Compare(nameof(Password), ErrorMessage = "As duas senhas precisam ser iguais.")]
        [DataType(DataType.Password)]
        public string Confirmation { get; set; } = "";
    }
}
