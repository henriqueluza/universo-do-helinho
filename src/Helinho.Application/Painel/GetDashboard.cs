using Helinho.Application.Abstractions;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public sealed record DashboardQuery(AgeBand? AgeBand, int Days, Guid? UnitId);

public sealed record QuizTypeAccuracy(QuizType QuizType, int Answers, int? AccuracyPercent);

public sealed record RecentActivity(string StudentName, QuizType QuizType, bool IsCorrect, DateTimeOffset AnsweredAt);

public sealed record UnitOption(Guid Id, string Name);

public sealed record DashboardResult(
    int ActiveStudents,
    int Answers,
    IReadOnlyList<QuizTypeAccuracy> AccuracyByType,
    IReadOnlyList<RecentActivity> Recent,
    IReadOnlyList<UnitOption> Units);

/// <summary>
/// Indicadores do dashboard do painel. "Aluno ativo" = aluno que respondeu ao menos uma pergunta no período.
/// O escopo de unidade vem do filtro global de <c>Student</c> (admin só vê a própria; master vê todas e pode
/// restringir por <see cref="DashboardQuery.UnitId"/>). A faixa etária é a do aluno hoje (dia civil de Brasília).
/// </summary>
public static class GetDashboard
{
    public static readonly int[] AllowedDays = [7, 30, 90];

    public const int DefaultDays = 30;

    private const int RecentLimit = 10;

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static int NormalizeDays(int days) => AllowedDays.Contains(days) ? days : DefaultDays;

    public static async Task<DashboardResult> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var isMaster = user.IsInRole(RoleNames.Master);
        var since = clock.UtcNow.AddDays(-NormalizeDays(query.Days));
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrasiliaOffset).DateTime);
        var unitId = isMaster ? query.UnitId : null;

        var students = db.Students.AsQueryable();
        if (unitId is { } unit)
        {
            students = students.Where(s => s.UnitId == unit);
        }

        if (query.AgeBand is { } band)
        {
            var (youngest, oldest) = BirthDateRange(band, today);
            students = students.Where(s => s.BirthDate <= youngest && s.BirthDate > oldest);
        }

        var answers = from a in db.QuizAnswers
                      where a.AnsweredAt >= since
                      join qs in db.QuizSessions on a.SessionId equals qs.Id
                      join s in students on qs.StudentId equals s.Id
                      select new { s.Id, s.FullName, qs.QuizType, a.IsCorrect, a.AnsweredAt };

        var byType = await answers
            .GroupBy(x => x.QuizType)
            .Select(g => new { QuizType = g.Key, Total = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .ToListAsync(cancellationToken);
        var active = await answers.Select(x => x.Id).Distinct().CountAsync(cancellationToken);
        var recent = await answers
            .OrderByDescending(x => x.AnsweredAt)
            .Take(RecentLimit)
            .Select(x => new RecentActivity(x.FullName, x.QuizType, x.IsCorrect, x.AnsweredAt))
            .ToListAsync(cancellationToken);
        var units = isMaster
            ? await db.Units.OrderBy(u => u.Name).Select(u => new UnitOption(u.Id, u.Name)).ToListAsync(cancellationToken)
            : [];

        var accuracy = Enum.GetValues<QuizType>()
            .Select(t => byType.FirstOrDefault(r => r.QuizType == t) is { } r
                ? new QuizTypeAccuracy(t, r.Total, (int)Math.Round(100.0 * r.Correct / r.Total, MidpointRounding.AwayFromZero))
                : new QuizTypeAccuracy(t, 0, null))
            .ToList();

        return new DashboardResult(active, byType.Sum(r => r.Total), accuracy, recent, units);
    }

    /// <summary>Nascidos até <c>Youngest</c> (já têm a idade mínima) e depois de <c>Oldest</c> (ainda não passaram da máxima).</summary>
    internal static (DateOnly Youngest, DateOnly Oldest) BirthDateRange(AgeBand band, DateOnly today)
    {
        var (min, max) = band switch
        {
            AgeBand.Age6To7 => (6, 7),
            AgeBand.Age8To10 => (8, 10),
            AgeBand.Age11To12 => (11, 12),
            _ => (13, 14),
        };
        return (today.AddYears(-min), today.AddYears(-(max + 1)));
    }
}
