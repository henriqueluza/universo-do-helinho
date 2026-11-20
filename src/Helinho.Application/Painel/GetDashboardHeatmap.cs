using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

public enum DayShift { Morning, Afternoon, Night }

/// <summary>Uma célula do mapa: respostas do dia da semana e turno (horário de Brasília) e a fração, de 0 a 1, em relação à
/// célula mais cheia (o <c>--v</c> da tela).</summary>
public sealed record HeatmapCell(DayOfWeek Day, DayShift Shift, int Answers, double Intensity);

/// <summary>Sempre 21 células, na ordem da tabela da tela: uma linha por turno, de segunda a domingo. <c>Peak</c> é a
/// célula mais cheia (no empate, a primeira nessa ordem) e é nulo sem respostas no período.</summary>
public sealed record DashboardHeatmap(DateOnly From, DateOnly To, IReadOnlyList<HeatmapCell> Cells, HeatmapCell? Peak)
{
    public bool IsEmpty => Peak is null;
}

/// <summary>
/// Bloco "Quando a turma joga": respostas por dia da semana e turno. Mesmo período (dias civis de Brasília), filtros e
/// escopo de unidade do <see cref="GetDashboardTrends"/>. O dia e a hora saem do instante da resposta convertido para
/// <c>America/Sao_Paulo</c> no banco: resposta às 23:30 de segunda em Brasília é de segunda, não da terça em UTC.
/// Uma consulta agregada (até 7 × 24 linhas); nenhum dado de aluno sai daqui.
/// PROPOSTA (a validar com a contratante): o número é o total do período, não a média por semana que a preview cita.
/// </summary>
public static class GetDashboardHeatmap
{
    private static readonly DayOfWeek[] WeekDays = [.. Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7))]; // segunda a domingo

    /// <summary>PROPOSTA (a validar com a contratante): manhã 06:00–11:59, tarde 12:00–17:59, noite no restante. A
    /// madrugada (00:00–05:59) conta como noite do próprio dia civil.</summary>
    public static DayShift ShiftOf(int hour) =>
        hour is >= 6 and < 12 ? DayShift.Morning : hour is >= 12 and < 18 ? DayShift.Afternoon : DayShift.Night;

    public static async Task<DashboardHeatmap> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(clock.UtcNow));
        var from = today.AddDays(1 - GetDashboard.NormalizeDays(query.Days));
        var (start, end) = (GetDashboardTrends.StartOf(from), GetDashboardTrends.StartOf(today.AddDays(1)));

        var byHour = await (from a in db.QuizAnswers
                            where a.AnsweredAt >= start && a.AnsweredAt < end
                            join qs in db.QuizSessions on a.SessionId equals qs.Id
                            join s in GetDashboard.ScopedStudents(db, user, query, today) on qs.StudentId equals s.Id
                            select TimeZoneInfo.ConvertTimeBySystemTimeZoneId(a.AnsweredAt.UtcDateTime, GetDashboardTrends.TimeZoneId))
            .GroupBy(local => new { local.DayOfWeek, local.Hour })
            .Select(g => new { g.Key.DayOfWeek, g.Key.Hour, Answers = g.Count() })
            .ToListAsync(cancellationToken);

        var answers = byHour.ToLookup(h => (h.DayOfWeek, ShiftOf(h.Hour)), h => h.Answers);
        var max = answers.Select(cell => cell.Sum()).DefaultIfEmpty().Max();
        var cells = (from shift in Enum.GetValues<DayShift>()
                     from day in WeekDays
                     let count = answers[(day, shift)].Sum()
                     select new HeatmapCell(day, shift, count, max == 0 ? 0 : (double)count / max)).ToList();

        return new DashboardHeatmap(from, today, cells, max == 0 ? null : cells.First(c => c.Answers == max));
    }
}
