using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Unidades;

public sealed class NovaModel(IAppDbContext db) : PageModel
{
    [BindProperty]
    public UnitInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        db.Units.Add(new Unit(Input.Name.Trim()));
        await db.SaveChangesAsync(cancellationToken);
        return Redirect("/Unidades");
    }

    public sealed class UnitInput
    {
        [Required(ErrorMessage = "Informe o nome da unidade.")]
        [StringLength(120, ErrorMessage = "O nome pode ter no máximo 120 caracteres.")]
        [Display(Name = "Nome da unidade")]
        public string Name { get; set; } = "";
    }
}
