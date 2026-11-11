using System.ComponentModel.DataAnnotations;
using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Helinho.Infrastructure.Persistence;
using Helinho.Web.Panel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Web.Pages.Administradores;

public sealed class NovoModel(UserManager<AppUser> users, IAppDbContext db) : PageModel
{
    [BindProperty]
    public AdminInput Input { get; set; } = new();

    public IReadOnlyList<Unit> Units { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadUnitsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadUnitsAsync(cancellationToken);
        var password = Input.Password;
        Input.Password = ""; // a senha nunca volta na tela

        if (Input.UnitId is { } unitId && Units.All(u => u.Id != unitId))
        {
            ModelState.AddModelError("Input.UnitId", "Selecione uma unidade válida.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // A verificação "um administrador ativo por unidade" e a criação rodam sob o mesmo lock da unidade.
        await using var tx = await db.BeginLockedTransactionAsync($"admin-unit:{Input.UnitId}", cancellationToken);
        if (await HasActiveAdminAsync(Input.UnitId!.Value))
        {
            ModelState.AddModelError("Input.UnitId", "Esta unidade já tem um administrador ativo.");
            return Page();
        }

        var user = new AppUser { UserName = Input.UserName.Trim(), Email = Input.Email.Trim(), UnitId = Input.UnitId };
        var created = await users.CreateAsync(user, password!);
        if (created.Succeeded)
        {
            created = await users.AddToRoleAsync(user, RoleNames.Admin);
        }

        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
            {
                var (field, message) = IdentityMensagens.Traduzir(error);
                ModelState.AddModelError(field, message);
            }

            return Page();
        }

        await tx.CommitAsync(cancellationToken);
        return Redirect("/Administradores");
    }

    private async Task<bool> HasActiveAdminAsync(Guid unitId)
    {
        var now = DateTimeOffset.UtcNow;
        var admins = await users.GetUsersInRoleAsync(RoleNames.Admin);
        return admins.Any(a => a.UnitId == unitId && (a.LockoutEnd is null || a.LockoutEnd <= now));
    }

    private async Task LoadUnitsAsync(CancellationToken cancellationToken) =>
        Units = await db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(cancellationToken);

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

        [Required(ErrorMessage = "Informe a senha inicial.")]
        [StringLength(128, ErrorMessage = "A senha pode ter no máximo 128 caracteres.")]
        public string Password { get; set; } = "";
    }
}
