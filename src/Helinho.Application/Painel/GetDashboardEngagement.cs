using System.Globalization;
using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

/// <summary>Um ponto do minigráfico. Média e sessões por aluno são nulas no dia sem sessão (não é zero).</summary>
public sealed record DailyEngagement(
    DateOnly Day, int? AverageSessionMinutes, decimal? SessionsPerStudent, int InactiveStudents, int CompletedQuests);

/// <summary><c>From</c> e <c>To</c> são dias civis de Brasília, os dois inclusivos. <c>LastDays</c> são sempre os
/// <see cref="GetDashboardEngagement.SparkDays"/> dias que terminam em <c>To</c>, em ordem.</summary>
public sealed record DashboardEngagement(
    DateOnly From, DateOnly To,
    Indicator AverageSessionMinutes, Indicator SessionsPerStudent, Indicator InactiveStudents, Indicator CompletedQuests,
    IReadOnlyList<DailyEngagement> LastDays)
{
    /// <summary>Nenhuma sessão iniciada ou concluída e nenhuma missão concluída no período.</summary>
    public bool IsEmpty => AverageSessionMinutes.Value is null && SessionsPerStudent.Value is null && CompletedQuests.Value is 0;
}

/// <summary>
/// Indicadores de sessão e engajamento do Painel Recreio, no mesmo período (dias civis de <c>America/Sao_Paulo</c>),
/// filtros e escopo de unidade do <see cref="GetDashboardTrends"/>. Seis consultas agregadas no banco, em número fixo;
/// nenhum resultado carrega nome ou dado cadastral de aluno.
/// <list type="bullet">
/// <item>Tempo médio: sessões concluídas no período (pelo dia de <c>CompletedAt</c>), em minutos inteiros.</item>
/// <item>Sessões por aluno: sessões iniciadas no período ÷ alunos que iniciaram sessão. O valor é inteiro e a variação
/// tem uma casa (como na preview); a série diária tem uma casa.</item>
/// <item>Sem jogar: alunos do escopo sem resposta nos <see cref="InactiveAfterDays"/> dias civis que terminam no dia de
/// referência; o valor anterior é a mesma contagem na véspera do período e a variação é absoluta.</item>
/// <item>Missões concluídas: progressos com <c>Current &gt;= Target</c> em dias do período.</item>
/// </list>
/// </summary>
public static class GetDashboardEngagement
{
    /// <summary>PROPOSTA (a validar com a contratante): sessão aberta não entra no tempo médio, e sessão concluída que
    /// durou mais que isto também não (foi abandonada e retomada depois; a "duração" seria de horas ou dias).</summary>
    public const int MaxSessionMinutes = 60;

    /// <summary>PROPOSTA (limiar da preview): dias sem responder para o aluno contar como "sem jogar". Quem nunca
    /// respondeu também conta, mesmo recém-cadastrado (o aluno não tem data de cadastro).</summary>
    public const int InactiveAfterDays = 7;

    public const int SparkDays = 7;
    public const string NoCompletedSessions = "sem sessões concluídas no período";
    public const string NoSessions = "sem sessões no período";

    public static async Task<DashboardEngagement> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var days = GetDashboard.NormalizeDays(query.Days);
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(clock.UtcNow));
        var from = today.AddDays(1 - days);
        var previousFrom = from.AddDays(-days);
        var (start, previousStart, end) =
            (GetDashboardTrends.StartOf(from), GetDashboardTrends.StartOf(previousFrom), GetDashboardTrends.StartOf(today.AddDays(1)));
        var students = GetDashboard.ScopedStudents(db, user, query, today);
        var sparkDays = Enumerable.Range(0, SparkDays).Select(i => today.AddDays(i + 1 - SparkDays)).ToArray();
        DateOnly[] referenceDays = [from.AddDays(-1), .. sparkDays]; // a véspera do período é sempre o dia mais antigo
        var oldestAnswer = GetDashboardTrends.StartOf(referenceDays[0].AddDays(1 - InactiveAfterDays));
        var maxDuration = TimeSpan.FromMinutes(MaxSessionMinutes);

        var sessions = from qs in db.QuizSessions
                       join s in students on qs.StudentId equals s.Id
                       select new { qs.StudentId, qs.StartedAt, qs.CompletedAt };
        var started = sessions.Where(x => x.StartedAt >= previousStart && x.StartedAt < end);

        var startedByDay = await started
            .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.StartedAt.UtcDateTime, GetDashboardTrends.TimeZoneId)))
            .Select(g => new { Day = g.Key, Sessions = g.Count(), Students = g.Select(x => x.StudentId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var startedByPeriod = await started
            .GroupBy(x => x.StartedAt >= start)
            .Select(g => new { IsCurrent = g.Key, Sessions = g.Count(), Students = g.Select(x => x.StudentId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var completedByDay = await sessions
            .Where(x => x.CompletedAt >= previousStart && x.CompletedAt < end && x.CompletedAt - x.StartedAt <= maxDuration)
            .Select(x => new { x.StartedAt, CompletedAt = x.CompletedAt ?? x.StartedAt }) // o filtro acima já garante o fim
            .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.CompletedAt.UtcDateTime, GetDashboardTrends.TimeZoneId)))
            .Select(g => new { Day = g.Key, Sessions = g.Count(), Seconds = g.Sum(x => (x.CompletedAt - x.StartedAt).TotalSeconds) })
            .ToListAsync(cancellationToken);
        var questsByDay = await (from p in db.QuestProgress
                                 where p.Day >= previousFrom && p.Day <= today
                                 join q in db.DailyQuests on p.QuestId equals q.Id
                                 where p.Current >= q.Target
                                 join s in students on p.StudentId equals s.Id
                                 group p by p.Day into g
                                 select new { Day = g.Key, Completed = g.Count() }).ToListAsync(cancellationToken);
        var scoped = await students.CountAsync(cancellationToken);
        var playedByDay = await (from a in db.QuizAnswers
                                 where a.AnsweredAt >= oldestAnswer && a.AnsweredAt < end
                                 join qs in db.QuizSessions on a.SessionId equals qs.Id
                                 join s in students on qs.StudentId equals s.Id
                                 let day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(a.AnsweredAt.UtcDateTime, GetDashboardTrends.TimeZoneId))
                                 from reference in referenceDays.Where(r => day <= r && day > r.AddDays(-InactiveAfterDays))
                                 group s.Id by reference into g
                                 select new { Day = g.Key, Students = g.Distinct().Count() }).ToListAsync(cancellationToken);

        var (startedOn, completedOn, questsOn) = (startedByDay.ToLookup(x => x.Day), completedByDay.ToLookup(x => x.Day), questsByDay.ToLookup(x => x.Day));
        int Inactive(DateOnly day) => scoped - playedByDay.Where(x => x.Day == day).Sum(x => x.Students);
        int? Minutes(bool current)
        {
            var rows = completedByDay.Where(x => (x.Day >= from) == current).ToList();
            return AverageMinutes(rows.Sum(x => x.Seconds), rows.Sum(x => x.Sessions));
        }
        decimal? PerStudent(bool current) =>
            Ratio(startedByPeriod.Where(x => x.IsCurrent == current).Sum(x => x.Sessions), startedByPeriod.Where(x => x.IsCurrent == current).Sum(x => x.Students));
        int Quests(bool current) => questsByDay.Where(x => (x.Day >= from) == current).Sum(x => x.Completed);

        var (minutes, previousMinutes, perStudent, previousPerStudent) = (Minutes(true), Minutes(false), PerStudent(true), PerStudent(false));
        var (inactive, previousInactive) = (Inactive(today), Inactive(referenceDays[0]));
        return new DashboardEngagement(
            from, today,
            new Indicator(minutes, previousMinutes, Compare(minutes, previousMinutes, NoCompletedSessions, (c, p) => Trend.Signed(c - p, " min", " min"))),
            new Indicator(Whole(perStudent), Whole(previousPerStudent), Compare(perStudent, previousPerStudent, NoSessions, OneDecimal)),
            new Indicator(inactive, previousInactive, Trend.Signed(inactive - previousInactive, "", "")),
            Indicator.Count(Quests(true), Quests(false)),
            [.. sparkDays.Select(d => new DailyEngagement(
                d,
                AverageMinutes(completedOn[d].Sum(x => x.Seconds), completedOn[d].Sum(x => x.Sessions)),
                Ratio(startedOn[d].Sum(x => x.Sessions), startedOn[d].Sum(x => x.Students)),
                Inactive(d),
                questsOn[d].Sum(x => x.Completed)))]);
    }

    /// <summary>Sem sessão não há média (nulo), em vez de dividir por zero.</summary>
    private static int? AverageMinutes(double seconds, int sessions) =>
        sessions == 0 ? null : (int)Math.Round(seconds / 60 / sessions, MidpointRounding.AwayFromZero);

    private static decimal? Ratio(int sessions, int students) =>
        students == 0 ? null : Math.Round((decimal)sessions / students, 1, MidpointRounding.AwayFromZero);

    private static int? Whole(decimal? value) => value is { } v ? (int)Math.Round(v, MidpointRounding.AwayFromZero) : null;

    private static Trend Compare<T>(T? current, T? previous, string noData, Func<T, T, Trend> signed)
        where T : struct =>
        current is not { } c ? new Trend(TrendDirection.None, noData)
        : previous is not { } p ? new Trend(TrendDirection.None, Trend.NoBaseline)
        : signed(c, p);

    /// <summary>Diferença com uma casa e vírgula ("+0,6"), sobre os valores já arredondados para a seta não contradizer o texto.</summary>
    private static Trend OneDecimal(decimal current, decimal previous) => current == previous
        ? new Trend(TrendDirection.Flat, Trend.Stable)
        : new Trend(
            current > previous ? TrendDirection.Up : TrendDirection.Down,
            $"{(current > previous ? "+" : "−")}{Math.Abs(current - previous).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')}");
}
