using Helinho.Domain.Entities;

namespace Helinho.Application.Administradores;

/// <summary>Conta de administrador como o caso de uso a enxerga (sem tipos do Identity).</summary>
public sealed record AdminAccount(string UserName, string Email, Guid? UnitId, DateTimeOffset? LockoutEnd);

public sealed record NewAdminAccount(string UserName, string Email, Guid UnitId, string Password);

/// <summary>Lock da unidade + transação, aberto por <see cref="IAdminAccountGateway.BeginUnitLockAsync"/>. Sem <c>CommitAsync</c>, tudo é desfeito ao descartar.</summary>
public interface IAdminUnitLock : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Porta para contas de administrador (Identity) e unidades, implementada na Infrastructure.</summary>
public interface IAdminAccountGateway
{
    Task<IReadOnlyList<Unit>> ListUnitsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAccount>> ListAdminsAsync(CancellationToken cancellationToken);

    Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken);

    /// <summary>Cria o usuário com o papel Admin. Devolve os códigos de erro do Identity; lista vazia = criado.</summary>
    Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken);
}
