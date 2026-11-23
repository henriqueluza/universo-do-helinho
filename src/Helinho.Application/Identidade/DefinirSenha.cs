namespace Helinho.Application.Identidade;

public enum DefinirSenhaStatus
{
    Definida,
    /// <summary>Conta inexistente, token malformado, de outra conta, vencido ou já usado: indistinguíveis de fora.</summary>
    LinkInvalido,
    /// <summary>A senha não passou na política do Identity; o link continua valendo.</summary>
    SenhaRecusada,
}

/// <summary><paramref name="ErrorCodes"/> traz os códigos do Identity quando a senha é recusada; vazio nos outros casos.</summary>
public sealed record DefinirSenhaResult(DefinirSenhaStatus Status, IReadOnlyList<string> ErrorCodes)
{
    public static DefinirSenhaResult Of(DefinirSenhaStatus status) => new(status, []);
}

/// <summary>Porta para o Identity: confere o token do link nos dois provedores (convite e redefinição) e grava a senha.</summary>
public interface IPasswordLinkGateway
{
    /// <summary>Confere o link sem consumi-lo e sem mudar a conta.</summary>
    Task<bool> IsValidAsync(Guid userId, string token, CancellationToken cancellationToken);

    /// <summary>Grava a senha se o link valer. A troca muda o carimbo de segurança: o link passa a ser de uso único.</summary>
    Task<DefinirSenhaResult> SetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);
}

/// <summary>
/// Caso de uso "definir senha com o link do e-mail" (convite e redefinição, ADR-0003 §10 e §11.3).
/// Recebe os valores como vieram da URL; o que não é id ou token bem formado vira "link inválido" sem consultar a conta.
/// </summary>
public sealed class DefinirSenhaHandler(IPasswordLinkGateway links)
{
    public async Task<bool> LinkValidoAsync(string? usuario, string? token, CancellationToken cancellationToken) =>
        Ler(usuario, token) is { } link && await links.IsValidAsync(link.UserId, link.Token, cancellationToken);

    public Task<DefinirSenhaResult> HandleAsync(string? usuario, string? token, string novaSenha, CancellationToken cancellationToken) =>
        Ler(usuario, token) is { } link
            ? links.SetPasswordAsync(link.UserId, link.Token, novaSenha, cancellationToken)
            : Task.FromResult(DefinirSenhaResult.Of(DefinirSenhaStatus.LinkInvalido));

    private static (Guid UserId, string Token)? Ler(string? usuario, string? token) =>
        Guid.TryParseExact(usuario, "D", out var userId) && LinkDeAcesso.LerToken(token) is { } lido ? (userId, lido) : null;
}
