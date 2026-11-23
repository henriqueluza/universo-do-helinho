using Helinho.Application.Abstractions;

namespace Helinho.Application.Administradores;

public sealed record AdministradorRow(Guid Id, string UserName, string Email, string UnitName, AdminSituacao Situacao);

/// <summary>Caso de uso da listagem de administradores, com o nome da unidade e a situação.</summary>
public sealed class ListarAdministradoresHandler(IAdminAccountGateway accounts, IAdminInviteJobs invites, IClock clock)
{
    public async Task<IReadOnlyList<AdministradorRow>> HandleAsync(CancellationToken cancellationToken)
    {
        var unitNames = (await accounts.ListUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var now = clock.UtcNow;
        var admins = await accounts.ListAdminsAsync(cancellationToken);
        var latest = await invites.LatestAsync(admins.Where(a => !a.HasPassword).Select(a => a.Id).ToList(), cancellationToken);
        return admins
            .Select(a => new AdministradorRow(
                a.Id, a.UserName, a.Email,
                a.UnitId is { } id && unitNames.TryGetValue(id, out var name) ? name : "—",
                ConviteAdministrador.Situacao(a, latest.GetValueOrDefault(a.Id), now)))
            .OrderBy(r => r.UnitName).ThenBy(r => r.UserName)
            .ToList();
    }
}
