using Helinho.Application.Identidade;
using Helinho.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Helinho.Infrastructure.Identidade;

/// <summary>Links de acesso sobre o Identity. O token nunca vai para log: os registros levam só o id da conta.</summary>
public sealed partial class PasswordLinkGateway(UserManager<AppUser> users, ILogger<PasswordLinkGateway> logger) : IPasswordLinkGateway
{
    public async Task<bool> IsValidAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        await FindAsync(userId, token, cancellationToken) is not null;

    public async Task<DefinirSenhaResult> SetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await FindAsync(userId, token, cancellationToken);
        if (user is null)
        {
            LogRecusado(logger);
            return DefinirSenhaResult.Of(DefinirSenhaStatus.LinkInvalido);
        }

        // O link já foi conferido acima. ResetPasswordAsync é o caminho público que valida a política, grava o hash e
        // troca o carimbo numa gravação só, com ou sem senha anterior; ele pede um token do provedor de redefinição,
        // gerado aqui e usado na hora. Dois envios simultâneos do mesmo link: o segundo falha no carimbo já trocado.
        var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), newPassword);
        if (result.Succeeded)
        {
            LogDefinida(logger, user.Id);
            return DefinirSenhaResult.Of(DefinirSenhaStatus.Definida);
        }

        var codes = result.Errors.Select(e => e.Code).ToList();
        return codes.Exists(c => c is "InvalidToken" or "ConcurrencyFailure")
            ? DefinirSenhaResult.Of(DefinirSenhaStatus.LinkInvalido)
            : new DefinirSenhaResult(DefinirSenhaStatus.SenhaRecusada, codes);
    }

    /// <summary>
    /// A conta, quando o token vale em algum dos dois provedores; nada na URL diz qual deles o emitiu.
    /// O convite de 72 horas só vale para conta sem senha: quem já tem senha troca por link de 1 hora (D4, §11.4).
    /// </summary>
    private async Task<AppUser?> FindAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); // o UserManager não recebe o token de cancelamento por chamada
        var user = await users.FindByIdAsync(userId.ToString());
        var vale = user is not null
            && (await users.VerifyUserTokenAsync(user, LinkDeAcesso.ProvedorRedefinicao, LinkDeAcesso.Finalidade, token)
                || (!await users.HasPasswordAsync(user)
                    && await users.VerifyUserTokenAsync(user, LinkDeAcesso.ProvedorConvite, LinkDeAcesso.Finalidade, token)));
        return vale ? user : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Senha definida por link de acesso para a conta {UserId}.")]
    private static partial void LogDefinida(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Link de acesso recusado ao definir senha.")]
    private static partial void LogRecusado(ILogger logger);
}
