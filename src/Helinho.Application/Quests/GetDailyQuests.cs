using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quests;

/// <summary>
/// Casos de uso do GET /quests/daily e /quests/daily-bonus. O "dia" é o dia civil de Brasília (UTC−3, sem horário
/// de verão desde 2019); <c>renews_at</c> é a próxima meia-noite de Brasília, em UTC. Missão genérica sai com
/// <c>quiz_type</c> "" (P6). Bônus: valores fixos do MVP (exemplo do contrato); a regra de concessão está fora de escopo.
/// </summary>
public static class GetDailyQuests
{
    public const int BonusRewardXp = 100;

    public const int BonusRequiredQuests = 3;

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static DailyBonusResponse Bonus() => new(BonusRewardXp, BonusRequiredQuests);

    public static async Task<DailyQuestsResponse?> HandleAsync(
        IAppDbContext db, ICurrentStudent current, IClock clock, CancellationToken cancellationToken)
    {
        var studentId = current.StudentId;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var exists = await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == studentId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var local = clock.UtcNow.ToOffset(BrasiliaOffset);
        var today = DateOnly.FromDateTime(local.DateTime);
        var renewsAt = new DateTimeOffset(local.Date.AddDays(1), BrasiliaOffset).UtcDateTime;

        var quests = await db.DailyQuests
            .OrderBy(q => q.Title)
            .ThenBy(q => q.Id)
            .Select(q => new
            {
                q.Id,
                q.Title,
                q.Target,
                q.RewardXp,
                q.QuizType,
                Progress = db.QuestProgress
                    .Where(p => p.StudentId == studentId && p.QuestId == q.Id && p.Day == today)
                    .Select(p => (int?)p.Current)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = quests
            .Select(q => new QuestItem(
                q.Id.ToString(), q.Title, Math.Clamp(q.Progress ?? 0, 0, q.Target), q.Target, q.RewardXp,
                q.QuizType?.ToString().ToLowerInvariant() ?? ""))
            .ToList();
        return new DailyQuestsResponse(renewsAt, items);
    }
}
