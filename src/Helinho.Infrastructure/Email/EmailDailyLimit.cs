using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.Email;

namespace Helinho.Infrastructure.Email;

/// <summary>
/// Raia do tratador de e-mail (ADR-0003 §15): os tipos que um registro atende e a parte do teto diário que eles podem
/// usar. São dois registros da mesma classe, cada um com captura e laço próprios, para que suspender os demais não
/// pare os urgentes.
/// </summary>
public sealed record EmailLane(IReadOnlyCollection<string> Types, int QuotaPercent)
{
    /// <summary>Redefinição e convite de administrador saem até o teto: ninguém fica sem conseguir entrar.</summary>
    public static EmailLane Urgent { get; } = new([EmailJobTypes.PasswordReset, EmailJobTypes.AdminInvite], 100);

    /// <summary>Os demais param em 80% do teto.</summary>
    public static EmailLane Others { get; } =
        new([EmailJobTypes.PasswordChanged, EmailJobTypes.StudentAccess, EmailJobTypes.GuardianEmailChanged], 80);
}

/// <summary><c>Email:DailyLimit</c> (§7 e §15): inteiro; <c>0</c> ou vazio = sem teto.</summary>
public sealed class EmailDailyLimitOptions
{
    public const string Key = "Email:DailyLimit";

    public string Value { get; set; } = "";

    /// <summary>Nulo quando o valor configurado não é um inteiro maior ou igual a zero.</summary>
    public int? Limit => Value.Length == 0 ? 0
        : int.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : null;
}

/// <summary>Quantos e-mails já saíram desde um instante. Porta para o teto ser testado sem banco.</summary>
public interface IEmailDailyCount
{
    Task<int> SentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Teto diário de envios (§15, D7). Conta as últimas 24 horas e, no limite da raia, manda esperar até a hora cheia
/// seguinte. Quem chama devolve <c>Suspend</c>: o trabalho volta a pendente sem gastar tentativa (§6.3).
/// </summary>
public sealed class EmailDailyLimit(int limit, IEmailDailyCount sent, IClock clock)
{
    public const string Code = "daily_limit";

    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>Nulo quando a raia pode enviar; senão, quanto falta para a hora cheia seguinte.</summary>
    public async Task<TimeSpan?> PauseForAsync(EmailLane lane, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return null;
        }

        var now = clock.UtcNow.ToUniversalTime();
        if (await sent.SentSinceAsync(now - Window, cancellationToken) < (long)limit * lane.QuotaPercent / 100)
        {
            return null;
        }

        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1) - now;
    }
}
