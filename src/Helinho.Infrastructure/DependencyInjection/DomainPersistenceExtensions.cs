using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.DependencyInjection;

public static class DomainPersistenceExtensions
{
    /// <summary>Nome fixo: isola as chaves desta aplicação e não varia com o caminho do contêiner. Trocar invalida cookies e tokens já emitidos.</summary>
    public const string DataProtectionApplicationName = "universo-do-helinho";

    /// <summary>Slice 1b: persistência. A connection string vem de ConnectionStrings__Default (variável de ambiente).</summary>
    public static IServiceCollection AddDomainPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));
        services.AddScoped<Helinho.Application.Abstractions.IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<Helinho.Application.Abstractions.IJobQueue, JobQueue>();

        // ADR-0002 §9: chaves na tabela DataProtectionKeys, para o cookie do painel sobreviver a um redeploy.
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<AppDbContext>();
        // O repositório do pacote escreve o XML da chave em log no nível Debug: a categoria nunca desce de Information.
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.DataProtection.EntityFrameworkCore", LogLevel.Information));
        return services;
    }
}
