using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Web.Pages.Unidades;

public sealed class IndexModel(IAppDbContext db) : PageModel
{
    public IReadOnlyList<Unit> Units { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Units = await db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(cancellationToken);
}
