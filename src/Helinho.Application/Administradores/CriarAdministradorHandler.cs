using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;

namespace Helinho.Application.Administradores;

public enum CriarAdministradorStatus
{
    Created,
    InvalidUnit,
    UnitHasActiveAdmin,
    Rejected,
}

public sealed record CriarAdministradorResult(CriarAdministradorStatus Status, IReadOnlyList<string> ErrorCodes)
{
    public static CriarAdministradorResult Of(CriarAdministradorStatus status) => new(status, []);
}

/// <summary>Caso de uso "criar administrador": no máximo um ativo (sem bloqueio vigente) por unidade; a conta nasce sem senha, com o convite na fila (ADR-0003 §11.2).</summary>
public sealed class CriarAdministradorHandler(IAdminAccountGateway accounts, IJobQueue queue, IClock clock)
{
    public Task<IReadOnlyList<Unit>> ListUnitsAsync(CancellationToken cancellationToken) =>
        accounts.ListUnitsAsync(cancellationToken);

    public async Task<CriarAdministradorResult> HandleAsync(NewAdminAccount account, CancellationToken cancellationToken)
    {
        var units = await accounts.ListUnitsAsync(cancellationToken);
        if (units.All(u => u.Id != account.UnitId))
        {
            return CriarAdministradorResult.Of(CriarAdministradorStatus.InvalidUnit);
        }

        // A checagem e a criação rodam sob o mesmo lock da unidade; dois cadastros paralelos não passam juntos.
        await using var unitLock = await accounts.BeginUnitLockAsync(account.UnitId, cancellationToken);
        var admins = await accounts.ListAdminsAsync(cancellationToken);
        if (admins.Any(a => a.UnitId == account.UnitId && IsActive(a, clock.UtcNow)))
        {
            return CriarAdministradorResult.Of(CriarAdministradorStatus.UnitHasActiveAdmin);
        }

        // E-mail único entre contas do painel (D11): a recuperação de senha precisa achar exatamente uma conta.
        var errors = await accounts.PanelEmailInUseAsync(account.Email, cancellationToken)
            ? ["DuplicateEmail"]
            : await accounts.CreateAdminAsync(account, cancellationToken);
        if (errors.Count > 0)
        {
            return new CriarAdministradorResult(CriarAdministradorStatus.Rejected, errors);
        }

        // §5.5: o convite entra no contexto sem salvar e é gravado com a conta, no commit do lock.
        await queue.EnqueueAsync(ConviteAdministrador.Trabalho(account.Id, clock.UtcNow), cancellationToken);
        await unitLock.CommitAsync(cancellationToken);
        return CriarAdministradorResult.Of(CriarAdministradorStatus.Created);
    }

    /// <summary>Ativo = sem bloqueio (lockout) vigente.</summary>
    public static bool IsActive(AdminAccount admin, DateTimeOffset now) => admin.LockoutEnd is null || admin.LockoutEnd <= now;
}
