using Helinho.Application.Administradores;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helinho.Web.Pages.Administradores;

public sealed class IndexModel(ListarAdministradoresHandler listar) : PageModel
{
    public IReadOnlyList<AdministradorRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Rows = await listar.HandleAsync(cancellationToken);
}
