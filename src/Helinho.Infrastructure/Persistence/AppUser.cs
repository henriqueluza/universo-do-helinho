using Microsoft.AspNetCore.Identity;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Usuário do Identity (papéis Master, Admin e Student). Dados cadastrais ficam em entidade própria do domínio (1b).</summary>
public class AppUser : IdentityUser<Guid>
{
}
