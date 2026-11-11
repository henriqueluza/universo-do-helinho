using Helinho.Application.Abstractions;

namespace Helinho.Application.Administradores;

public sealed record AdministradorRow(string UserName, string Email, string UnitName, bool Active);

/// <summary>Caso de uso da listagem de administradores, com o nome da unidade e a situação.</summary>
public sealed class ListarAdministradoresHandler(IAdminAccountGateway accounts, IClock clock)
{
    public async Task<IReadOnlyList<AdministradorRow>> HandleAsync(CancellationToken cancellationToken)
    {
        var unitNames = (await accounts.ListUnitsAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        var now = clock.UtcNow;
        return (await accounts.ListAdminsAsync(cancellationToken))
            .Select(a => new AdministradorRow(
                a.UserName, a.Email,
                a.UnitId is { } id && unitNames.TryGetValue(id, out var name) ? name : "—",
                CriarAdministradorHandler.IsActive(a, now)))
            .OrderBy(r => r.UnitName).ThenBy(r => r.UserName)
            .ToList();
    }
}
