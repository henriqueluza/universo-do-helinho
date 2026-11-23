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
        new IdentityBuilder(typeof(AppUser), typeof(IdentityRole<Guid>), services)
            .AddTokenProvider<ConviteTokenProvider>(LinkDeAcesso.ProvedorConvite)
            .AddTokenProvider<RedefinicaoTokenProvider>(LinkDeAcesso.ProvedorRedefinicao);
        // O padrão ("Default") não está registrado; sem isto, ResetPasswordAsync não acha provedor.
        services.Configure<IdentityOptions>(o => o.Tokens.PasswordResetTokenProvider = LinkDeAcesso.ProvedorRedefinicao);
        services.AddPasswordLinks();
        return services;
    }

    public sealed class ConviteTokenOptions : DataProtectionTokenProviderOptions
    {
        public ConviteTokenOptions()
        {
            Name = LinkDeAcesso.ProvedorConvite;
            TokenLifespan = LinkDeAcesso.ValidadeConvite;
        }
    }

    public sealed class RedefinicaoTokenOptions : DataProtectionTokenProviderOptions
    {
        public RedefinicaoTokenOptions()
        {
            Name = LinkDeAcesso.ProvedorRedefinicao;
            TokenLifespan = LinkDeAcesso.ValidadeRedefinicao;
        }
    }

    public sealed class ConviteTokenProvider(
        IDataProtectionProvider dataProtection, IOptions<ConviteTokenOptions> options, ILogger<DataProtectorTokenProvider<AppUser>> logger)
        : DataProtectorTokenProvider<AppUser>(dataProtection, options, logger);

    public sealed class RedefinicaoTokenProvider(
        IDataProtectionProvider dataProtection, IOptions<RedefinicaoTokenOptions> options, ILogger<DataProtectorTokenProvider<AppUser>> logger)
        : DataProtectorTokenProvider<AppUser>(dataProtection, options, logger);
}
