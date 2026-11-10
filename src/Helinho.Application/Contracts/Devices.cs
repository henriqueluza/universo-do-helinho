namespace Helinho.Application.Contracts;

/// <summary>POST /devices/push-token (P9). Responde 204.</summary>
public sealed record PushTokenRequest(string Token, string Platform);
