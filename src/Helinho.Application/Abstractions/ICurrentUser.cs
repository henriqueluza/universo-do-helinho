namespace Helinho.Application.Abstractions;

/// <summary>Usuário autenticado da requisição atual (painel ou API).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    Guid? UnitId { get; }

    bool IsInRole(string role);
}
