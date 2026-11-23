using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Helinho.Infrastructure.DependencyInjection;

public static class DomainPersistenceExtensions
{
    /// <summary>Nome fixo: isola as chaves desta aplicação e não varia com o caminho do contêiner. Trocar invalida cookies e tokens já emitidos.</summary>
    public const string DataProtectionApplicationName = "universo-do-helinho";

    private const string GssEncryptionModeKeyword = "GSS Encryption Mode";

    /// <summary>
    /// Desliga a negociação de criptografia GSS (Kerberos, que este sistema não usa) quando a connection string não a pede.
    /// Com o padrão do Npgsql (<c>Prefer</c>), toda abertura que falha é repetida com o tempo que sobrou; na abertura
    /// síncrona (a do anel de chaves do Data Protection), sobrando menos de 1 ms o Npgsql 10.0.3 grava tempo limite 0
    /// no soquete, que significa "sem limite": com um servidor que aceita o TCP e não responde, a thread nunca volta.
    /// </summary>
    public static string? WithoutGssNegotiation(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        // Keys traz só o que a connection string declarou, pelo nome canônico (ContainsKey responde true para toda palavra-chave válida).
        if (!builder.Keys.Any(key => string.Equals(key, GssEncryptionModeKeyword, StringComparison.OrdinalIgnoreCase)))
        {
            builder.GssEncryptionMode = GssEncryptionMode.Disable;
        }

        return builder.ConnectionString;
    }

    /// <summary>Slice 1b: persistência. A connection string vem de ConnectionStrings__Default (variável de ambiente).</summary>
    public static IServiceCollection AddDomainPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(WithoutGssNegotiation(configuration.GetConnectionString("Default"))));
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
