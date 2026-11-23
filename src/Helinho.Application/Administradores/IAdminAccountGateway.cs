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

    /// <summary>A conta com o papel Admin, ou nulo.</summary>
    Task<AdminAccount?> FindAdminAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Troca o carimbo de segurança de uma conta ainda sem senha: os links já enviados deixam de valer. Falso = não há tal conta.</summary>
    Task<bool> RotateSecurityStampAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Apaga a conta de administrador que nunca teve senha. Falso = não há tal conta (ou ela acabou de ganhar senha).</summary>
    Task<bool> DeleteInvitedAdminAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IAdminUnitLock> BeginUnitLockAsync(Guid unitId, CancellationToken cancellationToken);

    /// <summary>Há conta do painel (Master ou Admin) com este e-mail? Dentro do lock da unidade, trava também o e-mail até o fim da transação.</summary>
    Task<bool> PanelEmailInUseAsync(string email, CancellationToken cancellationToken);

    /// <summary>Cria o usuário, sem senha, com o papel Admin. Devolve os códigos de erro do Identity; lista vazia = criado.</summary>
    Task<IReadOnlyList<string>> CreateAdminAsync(NewAdminAccount account, CancellationToken cancellationToken);
}
