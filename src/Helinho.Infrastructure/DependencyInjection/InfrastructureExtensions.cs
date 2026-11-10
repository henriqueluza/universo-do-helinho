using Helinho.Application.Abstractions;
using Helinho.Infrastructure.Fakes;
using Helinho.Infrastructure.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Helinho.Infrastructure.DependencyInjection;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEmailSender, FakeEmailSender>();
        services.AddSingleton<IPushSender, FakePushSender>();
        services.AddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.AddSingleton<IApiSessionValidator, RejectAllSessionValidator>();
        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
