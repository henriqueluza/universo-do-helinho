using Helinho.Application.Identidade;
using Helinho.Infrastructure.DependencyInjection;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helinho.Web.Composition;

/// <summary>
/// Provedores de token dos links de acesso (ADR-0003 §10.1): convite, 72 horas, e redefinição, 1 hora.
/// <c>AddDefaultTokenProviders</c> não é usado: traria também os provedores de e-mail, telefone e autenticador.
/// </summary>
public static class TokensDeLink
{
    public static IServiceCollection AddTokensDeLink(this IServiceCollection services)
    {
        services.Configure<ConviteOptions>(o => (o.Name, o.TokenLifespan) = (LinkDeAcesso.ProvedorConvite, LinkDeAcesso.ValidadeConvite));
        services.Configure<RedefinicaoOptions>(o => (o.Name, o.TokenLifespan) = (LinkDeAcesso.ProvedorRedefinicao, LinkDeAcesso.ValidadeRedefinicao));
        new IdentityBuilder(typeof(AppUser), typeof(IdentityRole<Guid>), services)
            .AddTokenProvider<Provedor<ConviteOptions>>(LinkDeAcesso.ProvedorConvite)
            .AddTokenProvider<Provedor<RedefinicaoOptions>>(LinkDeAcesso.ProvedorRedefinicao);
        // O padrão ("Default") não está registrado; sem isto, ResetPasswordAsync não acha provedor.
        services.Configure<IdentityOptions>(o => o.Tokens.PasswordResetTokenProvider = LinkDeAcesso.ProvedorRedefinicao);
        services.AddPasswordLinks();
        return services;
    }

    public sealed class ConviteOptions : DataProtectionTokenProviderOptions;

    public sealed class RedefinicaoOptions : DataProtectionTokenProviderOptions;

    /// <summary>Um provedor por classe de opções: cada um protege com a própria finalidade (o nome) e tem a própria validade.</summary>
    public sealed class Provedor<TOptions>(
        IDataProtectionProvider dataProtection, IOptions<TOptions> options, ILogger<DataProtectorTokenProvider<AppUser>> logger)
        : DataProtectorTokenProvider<AppUser>(dataProtection, options, logger)
        where TOptions : DataProtectionTokenProviderOptions, new();
}
