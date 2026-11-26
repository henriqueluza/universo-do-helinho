using Helinho.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Ai;

/// <summary>
/// Pedido recusado por cota (ADR-0004 §13). <paramref name="Global"/>: o teto de todos, não o da pessoa.
/// <paramref name="AvailableAt"/> é quando volta a haver saldo para este pedido; <c>null</c> se ele é maior que o limite.
/// </summary>
public sealed record AiQuotaExceeded(bool Global, int Limit, int Remaining, DateTimeOffset? AvailableAt);

/// <summary>
/// Cota por administrador e teto global, em sugestões pedidas, numa janela deslizante de 24 horas. Todo pedido conta,
/// inclusive o que falhou. A checagem e a criação do pedido não são serializadas: dois pedidos simultâneos podem passar
/// do limite em um pedido cada (limite conhecido, §13); o teto firme é o do console do provedor.
/// </summary>
public static class AiQuota
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static async Task<AiQuotaExceeded?> CheckAsync(
        IAppDbContext db, AiOptions options, Guid userId, int count, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.ToUniversalTime() - Window;
        var used = await db.AiGenerationJobs.AsNoTracking().Where(j => j.CreatedAt > since)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new Usage(j.RequestedByUserId, j.CreatedAt, j.RequestedCount))
            .ToListAsync(cancellationToken);
        return Exceeded([.. used.Where(u => u.UserId == userId)], options.DailyQuotaPerAdmin, count, global: false)
            ?? Exceeded(used, options.DailyGlobalLimit, count, global: true);
    }

    private static AiQuotaExceeded? Exceeded(IReadOnlyList<Usage> used, int limit, int count, bool global)
    {
        var total = used.Sum(u => u.Count);
        var missing = total + count - limit;
        if (missing <= 0)
        {
            return null;
        }

        // Os pedidos mais antigos saem da janela primeiro: o saldo volta quando saírem sugestões suficientes.
        DateTimeOffset? availableAt = null;
        foreach (var usage in used)
        {
            missing -= usage.Count;
            if (missing <= 0)
            {
                availableAt = usage.CreatedAt + Window;
                break;
            }
        }

        return new AiQuotaExceeded(global, limit, Math.Max(0, limit - total), availableAt);
    }

    private sealed record Usage(Guid UserId, DateTimeOffset CreatedAt, int Count);
}
