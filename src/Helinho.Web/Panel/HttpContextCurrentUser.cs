using System.Security.Claims;
using Helinho.Application.Abstractions;

namespace Helinho.Web.Panel;

/// <summary>ICurrentUser a partir das claims da requisição (papel e unidade).</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? UnitId => ParseGuid(Principal?.FindFirstValue(AppClaims.UnitId));

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
