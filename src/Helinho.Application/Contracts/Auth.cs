namespace Helinho.Application.Contracts;

/// <summary>POST /login (P1). `Login` aceita matrícula, usuário ou e-mail.</summary>
public sealed record LoginRequest(string Login, string Password);

/// <summary>Sem perfil junto, de propósito. `ExpiresAt` em UTC (P2, P7).</summary>
public sealed record LoginResponse(string Token, DateTime ExpiresAt, string UserId);
