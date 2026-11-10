using Microsoft.AspNetCore.Identity;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Usuário do Identity (papéis Master, Admin e Student). Dados cadastrais ficam em <c>Student</c>.</summary>
public class AppUser : IdentityUser<Guid>
{
    /// <summary>Unidade do administrador ou do aluno; nulo para o master.</summary>
    public Guid? UnitId { get; set; }
}
