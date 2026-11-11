namespace Helinho.Application.Identidade;

/// <summary>Configuração da sessão da API (P2) e do login. Seção "Api:Auth".</summary>
public sealed class ApiAuthOptions
{
    public const string Section = "Api:Auth";

    /// <summary>Validade do token (P2: 8 h, sem refresh).</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Tentativas erradas seguidas até o bloqueio da conta (T3).</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Limite de taxa de POST /api/login, por IP, por janela (T3).</summary>
    public int LoginRateLimit { get; set; } = 10;

    public TimeSpan LoginRateWindow { get; set; } = TimeSpan.FromMinutes(1);
}
