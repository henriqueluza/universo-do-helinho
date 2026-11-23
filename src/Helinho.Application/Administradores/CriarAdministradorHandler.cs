using Helinho.Application.Abstractions;
using Helinho.Domain.Entities;

namespace Helinho.Application.Administradores;

public enum CriarAdministradorStatus
{
    Created,
    InvalidUnit,
    UnitHasActiveAdmin,
    EmailInUse,
    Rejected,
}

public sealed record CriarAdministradorResult(CriarAdministradorStatus Status, IReadOnlyList<string> ErrorCodes)
{
    public static CriarAdministradorResult Of(CriarAdministradorStatus status) => new(status, []);
}

/// <summary>Caso de uso "criar administrador": uma unidade tem no máximo um administrador ativo (sem bloqueio vigente).</summary>
public sealed class CriarAdministradorHandler(IAdminAccountGateway accounts, IClock clock)
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

        // E-mail repetido quebraria o login e a recuperação por e-mail das duas contas (#184).
        if (await accounts.EmailInUseAsync(account.Email, cancellationToken))
        {
            return CriarAdministradorResult.Of(CriarAdministradorStatus.EmailInUse);
        }

        var errors = await accounts.CreateAdminAsync(account, cancellationToken);
        if (errors.Count > 0)
        {
            return new CriarAdministradorResult(CriarAdministradorStatus.Rejected, errors);
        }

        await unitLock.CommitAsync(cancellationToken);
        return CriarAdministradorResult.Of(CriarAdministradorStatus.Created);
    }

    /// <summary>Ativo = sem bloqueio (lockout) vigente.</summary>
    public static bool IsActive(AdminAccount admin, DateTimeOffset now) => admin.LockoutEnd is null || admin.LockoutEnd <= now;
}
