using Helinho.Application.Abstractions;
using Helinho.Domain;

namespace Helinho.Application.Ai;

/// <summary>
/// Quem pode pedir e revisar sugestões da IA: Master e Admin, como na política <c>Panel</c> (ADR-0004 §12 e §15).
/// A borda já exige a política; os casos de uso conferem de novo, porque também são chamados fora de uma página.
/// </summary>
public static class AiAccess
{
    /// <returns>O identificador do administrador autenticado, ou <c>null</c> se não há um.</returns>
    public static Guid? Reviewer(ICurrentUser user) =>
        user.IsAuthenticated && (user.IsInRole(RoleNames.Master) || user.IsInRole(RoleNames.Admin)) ? user.UserId : null;
}
