using Helinho.Application.Abstractions;

namespace Helinho.Infrastructure.Fakes;

/// <summary>Usuário atual vazio. A implementação real, baseada em HttpContext, nasce na frente de Identidade.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? UnitId => null;

    public bool IsInRole(string role) => false;
}
