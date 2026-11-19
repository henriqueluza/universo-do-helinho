using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public enum TrendDirection { None, Up, Down, Flat }

/// <summary>
/// Variação em relação ao período anterior: direção (seta) e texto pronto, para nunca depender só de cor.
/// <see cref="TrendDirection.None"/> = sem base de comparação: a tela não desenha seta, só o texto.
/// </summary>
public sealed record Trend(TrendDirection Direction, string Text)
{
    /// <summary>PROPOSTA (texto a validar com a contratante): o que a tela diz quando o período anterior não tem dado.</summary>
    public const string NoBaseline = "sem dados no período anterior";
    public const string NoData = "sem respostas no período";
    public const string Stable = "estável";

    /// <summary>Variação percentual de uma contagem. Período anterior zerado não divide: devolve <see cref="NoBaseline"/>.</summary>
    public static Trend OfCount(int current, int previous) => previous == 0
        ? new Trend(TrendDirection.None, NoBaseline)
        : Signed((int)Math.Round(100.0 * (current - previous) / previous, MidpointRounding.AwayFromZero), "%", "%");

    /// <summary>Diferença em pontos percentuais entre duas taxas; taxa nula = período sem respostas.</summary>
    public static Trend OfPoints(int? current, int? previous) =>
        current is not { } c ? new Trend(TrendDirection.None, NoData)
        : previous is not { } p ? new Trend(TrendDirection.None, NoBaseline)
        : Signed(c - p, " pt", " pts");

    private static Trend Signed(int delta, string one, string many) => delta == 0
        ? new Trend(TrendDirection.Flat, Stable)
        : new Trend(
            delta > 0 ? TrendDirection.Up : TrendDirection.Down,
            $"{(delta > 0 ? "+" : "−")}{Math.Abs(delta)}{(Math.Abs(delta) == 1 ? one : many)}");
}

/// <summary>Um número do período, o mesmo número no período anterior e a variação. Taxa sem respostas é nula.</summary>
public sealed record Indicator(int? Value, int? Previous, Trend Trend)
{
    public static Indicator Count(int current, int previous) => new(current, previous, Trend.OfCount(current, previous));
    public static Indicator Rate(int? current, int? previous) => new(current, previous, Trend.OfPoints(current, previous));
}

/// <summary>Um dia civil do período e o dia de mesma posição no período anterior (as duas linhas do gráfico).</summary>
public sealed record DailyAnswers(DateOnly Day, int Answers, int Correct, DateOnly PreviousDay, int PreviousAnswers)
{
    public int? AccuracyPercent => GetDashboardTrends.Percent(Correct, Answers);
}

/// <summary><c>From</c> e <c>To</c> são dias civis de Brasília, os dois inclusivos.</summary>
public sealed record DashboardTrends(
    DateOnly From, DateOnly To,
    Indicator Answers, Indicator CorrectAnswers, Indicator AccuracyPercent, Indicator ActiveStudents,
    IReadOnlyList<DailyAnswers> AnswersByDay, IReadOnlyList<QuizTypeAccuracy> AccuracyByType)
{
    /// <summary>Sem respostas no período: a tela mostra "Nenhuma resposta neste período.".</summary>
    public bool IsEmpty => Answers.Value is 0;
}

/// <summary>
/// Blocos do Painel Recreio que comparam o período com o anterior: indicadores, respostas por dia e acertos por tipo.
/// Período = os N dias civis de Brasília que terminam hoje (N de <see cref="DashboardQuery.Days"/>); o anterior são os
/// N dias imediatamente antes. Cada dia vai de 00:00 (inclusive) a 00:00 do dia seguinte (exclusive), em
/// <c>America/Sao_Paulo</c>. Mesmos filtros e mesmo escopo de unidade do <see cref="GetDashboard"/>. Duas consultas
/// agregadas no banco; nenhum resultado carrega nome ou dado cadastral de aluno.
/// </summary>
public static class GetDashboardTrends
{
    public const string TimeZoneId = "America/Sao_Paulo";

    public static async Task<DashboardTrends> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var days = GetDashboard.NormalizeDays(query.Days);
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(clock.UtcNow));
        var from = today.AddDays(1 - days);
        var previousFrom = from.AddDays(-days);
        var (start, previousStart, end) = (StartOf(from), StartOf(previousFrom), StartOf(today.AddDays(1)));

        var answers = from a in db.QuizAnswers
                      where a.AnsweredAt >= previousStart && a.AnsweredAt < end
                      join qs in db.QuizSessions on a.SessionId equals qs.Id
                      join s in GetDashboard.ScopedStudents(db, user, query, today) on qs.StudentId equals s.Id
                      select new { StudentId = s.Id, qs.QuizType, a.IsCorrect, a.AnsweredAt };

        var cells = await answers
            .GroupBy(x => new
            {
                Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.AnsweredAt.UtcDateTime, TimeZoneId)),
                x.QuizType,
            })
            .Select(g => new { g.Key.Day, g.Key.QuizType, Total = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .ToListAsync(cancellationToken);
        var active = await answers
            .GroupBy(x => x.AnsweredAt >= start)
            .Select(g => new { IsCurrent = g.Key, Students = g.Select(x => x.StudentId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        var current = cells.Where(c => c.Day >= from).ToList();
        var previous = cells.Where(c => c.Day < from).ToList();
        var (total, correct) = (current.Sum(c => c.Total), current.Sum(c => c.Correct));
        var (previousTotal, previousCorrect) = (previous.Sum(c => c.Total), previous.Sum(c => c.Correct));

        var onDay = cells.ToLookup(c => c.Day);
        var byDay = Enumerable.Range(0, days)
            .Select(i => (Day: from.AddDays(i), Previous: previousFrom.AddDays(i)))
            .Select(d => new DailyAnswers(
                d.Day, onDay[d.Day].Sum(c => c.Total), onDay[d.Day].Sum(c => c.Correct), d.Previous, onDay[d.Previous].Sum(c => c.Total)))
            .ToList();
        var ofType = current.ToLookup(c => c.QuizType);
        var byType = Enum.GetValues<QuizType>()
            .Select(t => new QuizTypeAccuracy(t, ofType[t].Sum(c => c.Total), Percent(ofType[t].Sum(c => c.Correct), ofType[t].Sum(c => c.Total))))
            .ToList();

        return new DashboardTrends(
            from, today,
            Indicator.Count(total, previousTotal),
            Indicator.Count(correct, previousCorrect),
            Indicator.Rate(Percent(correct, total), Percent(previousCorrect, previousTotal)),
            Indicator.Count(active.Where(a => a.IsCurrent).Sum(a => a.Students), active.Where(a => !a.IsCurrent).Sum(a => a.Students)),
            byDay, byType);
    }

    /// <summary>Percentual inteiro (meio arredonda para cima); sem respostas não há taxa.</summary>
    internal static int? Percent(int correct, int total) =>
        total == 0 ? null : (int)Math.Round(100.0 * correct / total, MidpointRounding.AwayFromZero);

    private static DateTimeOffset StartOf(DateOnly day) => BrasiliaTime.ToUtc(day.ToDateTime(TimeOnly.MinValue));
}
