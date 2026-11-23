using Helinho.Domain.Entities;

namespace Helinho.Application.Administradores;

/// <summary>Conta de administrador como o caso de uso a enxerga (sem tipos do Identity).</summary>
public sealed record AdminAccount(Guid Id, string UserName, string Email, Guid? UnitId, DateTimeOffset? LockoutEnd, bool HasPassword);

/// <summary>Conta nova: nasce sem senha; quem a define é o convidado, pelo link do convite (ADR-0003 §11.2).</summary>
public sealed record NewAdminAccount(string UserName, string Email, Guid UnitId)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>Lock da unidade + transação, aberto por <see cref="IAdminAccountGateway.BeginUnitLockAsync"/>. <c>CommitAsync</c> grava também o que ficou pendente no contexto (o convite na fila). Sem ele, tudo é desfeito ao descartar.</summary>
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

    /// <summary>
    /// Diz se alguma conta já usa o e-mail (sem diferenciar maiúsculas) e segura o endereço até o fim da transação
    /// do lock da unidade: o banco não tem índice único de e-mail, então a checagem e a criação precisam rodar juntas.
    /// </summary>
    Task<bool> EmailInUseAsync(string email, CancellationToken cancellationToken);

    /// <summary>Cria o usuário, sem senha, com o papel Admin. Devolve os códigos de erro do Identity; lista vazia = criado.</summary>
    Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken);
}
