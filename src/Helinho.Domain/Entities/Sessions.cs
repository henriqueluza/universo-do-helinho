namespace Helinho.Domain.Entities;

/// <summary>Sessão da API: só o hash do token é guardado (P2); validade de 8 h, revogada no logout (P3).</summary>
public class ApiSession
{
    private ApiSession()
    {
    }

    public ApiSession(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        UserId = Guard.Required(userId, "Usuário");
        TokenHash = Guard.Required(tokenHash, "Hash do token");
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = "";

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>Token de push do aparelho (P9). Só registra; não há envio no MVP.</summary>
public class PushToken(Guid userId, string token, string platform, DateTimeOffset registeredAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; } = userId;

    public string Token { get; private set; } = Guard.Required(token, "Token");

    public string Platform { get; private set; } = Guard.Required(platform, "Plataforma");

    public DateTimeOffset RegisteredAt { get; private set; } = registeredAt;
}
