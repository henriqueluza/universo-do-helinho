using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Contexto único do sistema. Partial: a Issue 1b acrescenta as entidades em arquivo próprio.</summary>
public partial class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
}
