using Helinho.Application.Abstractions;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

/// <summary>Linha da lista de Desempenho. <c>Answered</c> e <c>Correct</c> somam todas as respostas do aluno, de todos os tipos (#214).</summary>
public sealed record PerformanceRow(
    Guid Id, string FullName, string ClassName, AgeBand? Band, int Xp, int Level, string LevelName, int Answered, int Correct);

public sealed record PerformanceListResult(IReadOnlyList<PerformanceRow> Rows, int Total, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record QuizTypeScore(QuizType Type, int Answered, int Correct);

public sealed record UnlockedBadge(string Title, DateTimeOffset UnlockedAt);

public sealed record StudentPerformance(
    Guid Id, string FullName, string ClassName, string UnitName, AgeBand? Band,
    int Xp, int Level, string LevelName, int NextLevelXp,
    IReadOnlyList<QuizTypeScore> Scores, IReadOnlyList<UnlockedBadge> Badges)
{
    public int Answered => Scores.Sum(s => s.Answered);
}

/// <summary>
/// Desempenho do aluno no painel. O escopo de unidade vem do filtro global de <c>Student</c>: aluno de outra unidade
/// some da listagem e o detalhe devolve null (404), inclusive por URL direta. Não carrega CPF, RG nem endereço.
/// A lista traz respostas e acertos de cada aluno na mesma consulta paginada, contados só dos alunos visíveis pelo filtro de unidade.
/// </summary>
public static class StudentPerformanceQueries
{
    public const int PageSize = 20;

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static async Task<PerformanceListResult> ListAsync(
        IAppDbContext db, IClock clock, AgeBand? ageBand, int page, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrasiliaOffset).DateTime);
        var students = db.Students.AsQueryable();
        if (ageBand is { } band)
        {
            var (youngest, oldest) = GetDashboard.BirthDateRange(band, today);
            students = students.Where(s => s.BirthDate <= youngest && s.BirthDate > oldest);
        }

        var total = await students.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (total + PageSize - 1) / PageSize));
        var found = await students
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Skip((current - 1) * PageSize).Take(PageSize)
            .Select(s => new
            {
                s.Id,
                s.FullName,
                s.ClassName,
                s.BirthDate,
                s.Xp,
                Answered = (from a in db.QuizAnswers
                            join q in db.QuizSessions on a.SessionId equals q.Id
                            where q.StudentId == s.Id
                            select a).Count(),
                Correct = (from a in db.QuizAnswers
                           join q in db.QuizSessions on a.SessionId equals q.Id
                           where q.StudentId == s.Id && a.IsCorrect
                           select a).Count(),
            })
            .ToListAsync(cancellationToken);
        var rows = found.Select(s =>
        {
            var level = LevelPolicy.LevelFor(s.Xp);
            return new PerformanceRow(s.Id, s.FullName, s.ClassName, AgeBands.TryFromBirthDate(s.BirthDate, today),
                s.Xp, level, LevelPolicy.LevelName(level), s.Answered, s.Correct);
        }).ToList();
        return new PerformanceListResult(rows, total, current, PageSize);
    }

    public static async Task<StudentPerformance?> GetAsync(
        IAppDbContext db, IClock clock, Guid id, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(BrasiliaOffset).DateTime);
        var student = await (from s in db.Students
                             where s.Id == id
                             join u in db.Units on s.UnitId equals u.Id
                             select new { s.Id, s.FullName, s.ClassName, s.BirthDate, s.Xp, UnitName = u.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (student is null)
        {
            return null;
        }

        var perType = await db.QuizAnswers
            .Join(db.QuizSessions.Where(q => q.StudentId == id), a => a.SessionId, q => q.Id,
                (a, q) => new { q.QuizType, a.IsCorrect })
            .GroupBy(x => x.QuizType)
            .Select(g => new { Type = g.Key, Answered = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .ToListAsync(cancellationToken);
        var scores = Enum.GetValues<QuizType>()
            .Select(t => perType.FirstOrDefault(p => p.Type == t))
            .Select((p, i) => new QuizTypeScore(Enum.GetValues<QuizType>()[i], p?.Answered ?? 0, p?.Correct ?? 0))
            .ToList();

        var badges = await (from sb in db.StudentBadges
                            where sb.StudentId == id
                            join b in db.Badges on sb.BadgeId equals b.Id
                            orderby sb.UnlockedAt descending
                            select new { b.Title, sb.UnlockedAt })
            .ToListAsync(cancellationToken);

        var level = LevelPolicy.LevelFor(student.Xp);
        return new StudentPerformance(
            student.Id, student.FullName, student.ClassName, student.UnitName,
            AgeBands.TryFromBirthDate(student.BirthDate, today), student.Xp, level, LevelPolicy.LevelName(level),
            LevelPolicy.NextLevelXp(student.Xp), scores, badges.Select(b => new UnlockedBadge(b.Title, b.UnlockedAt)).ToList());
    }
}
