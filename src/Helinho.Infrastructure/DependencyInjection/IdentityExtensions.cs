using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class IdentityExtensions
{
    /// <summary>Liga os stores do Identity ao <see cref="AppDbContext"/>, sem expor o tipo concreto à camada Web.</summary>
    public static IdentityBuilder AddAppIdentityStores(this IdentityBuilder builder) =>
        builder.AddEntityFrameworkStores<AppDbContext>();
}
