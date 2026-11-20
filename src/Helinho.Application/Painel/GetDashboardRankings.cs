using Helinho.Application.Abstractions;
using Helinho.Application.LiveEvents;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Painel;

/// <summary>
/// Uma linha de "Questões que mais confundem". <c>QuestionId</c> alimenta o botão Revisar. <c>Statement</c> é texto
/// de conteúdo devolvido cru: quem renderiza faz o encode. A barra da tela (<c>--v</c>) é <c>AccuracyPercent / 100</c>.
/// Não leva alternativas nem a resposta correta.
/// </summary>
public sealed record ConfusingQuestion(Guid QuestionId, string Statement, QuizType QuizType, AgeBand AgeBand, int Answers, int AccuracyPercent);

/// <summary>
/// Uma linha de "Unidades que mais jogam"; a posição é a ordem na lista. <c>Share</c> é a fração de 0 a 1 das
/// respostas em relação à primeira colocada (o <c>--v</c> da barra), com duas casas.
/// </summary>
public sealed record UnitActivity(string UnitName, int Answers, int ActiveStudents, double Share);

/// <summary>
/// <c>TopUnits</c> nulo = o bloco não existe para quem pediu (qualquer papel que não seja master): a tela não desenha
/// o painel. Lista vazia = master sem respostas no período.
/// </summary>
public sealed record DashboardRankings(IReadOnlyList<ConfusingQuestion> ConfusingQuestions, IReadOnlyList<UnitActivity>? TopUnits);

/// <summary>
/// Os dois rankings do Painel Recreio. Mesmo período do <see cref="GetDashboardTrends"/> (os N dias civis de Brasília
/// que terminam hoje) e mesma faixa etária do aluno. "Questões" segue o escopo de unidade do
/// <see cref="GetDashboard.ScopedStudents"/>; "unidades" só é consultado para master. Agregação, ordenação e limite
/// no banco, uma consulta por bloco; nenhum resultado carrega nome ou dado cadastral de aluno.
/// </summary>
public static class GetDashboardRankings
{
    /// <summary>PROPOSTA (a validar com a contratante): tamanho dos dois rankings; a preview mostra 5 linhas.</summary>
    public const int Limit = 5;

    /// <summary>PROPOSTA (a validar com a contratante): com menos respostas que isto no período a questão não entra
    /// no ranking, para uma única resposta errada não virar "0% de acerto".</summary>
    public const int MinAnswers = 5;

    public static async Task<DashboardRankings> HandleAsync(
        IAppDbContext db, ICurrentUser user, IClock clock, DashboardQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(BrasiliaTime.ToLocal(clock.UtcNow));
        var start = GetDashboardTrends.StartOf(today.AddDays(1 - GetDashboard.NormalizeDays(query.Days)));
        var end = GetDashboardTrends.StartOf(today.AddDays(1));

        return new DashboardRankings(
            await ConfusingQuestionsAsync(db, user, query, today, start, end, cancellationToken),
            await TopUnitsAsync(db, user, query, today, start, end, cancellationToken));
    }

    /// <summary>
    /// PROPOSTA (critério de ordem): menor taxa de acerto exata primeiro (não a arredondada); no empate, a questão com
    /// mais respostas e, por fim, o id. Conta toda resposta do período, qualquer que seja o status atual da questão.
    /// </summary>
    private static async Task<IReadOnlyList<ConfusingQuestion>> ConfusingQuestionsAsync(
        IAppDbContext db, ICurrentUser user, DashboardQuery query, DateOnly today, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = await (
                from a in db.QuizAnswers
                where a.AnsweredAt >= start && a.AnsweredAt < end
                join qs in db.QuizSessions on a.SessionId equals qs.Id
                join s in GetDashboard.ScopedStudents(db, user, query, today) on qs.StudentId equals s.Id
                join q in db.Questions on a.QuestionId equals q.Id
                group a by new { q.Id, q.Statement, q.QuizType, q.AgeBand } into g
                select new { g.Key.Id, g.Key.Statement, g.Key.QuizType, g.Key.AgeBand, Answers = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .Where(r => r.Answers >= MinAnswers)
            .OrderBy(r => (double)r.Correct / r.Answers).ThenByDescending(r => r.Answers).ThenBy(r => r.Id)
            .Take(Limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new ConfusingQuestion(
            r.Id, r.Statement, r.QuizType, r.AgeBand, r.Answers, GetDashboardTrends.Percent(r.Correct, r.Answers) ?? 0))];
    }

    /// <summary>
    /// Comparação entre unidades (T1 do ADR-0001): só master. A recusa é a primeira linha e vale para qualquer outro
    /// papel, antes de montar a consulta; <c>Units</c> não tem filtro global, então é esta checagem que impede um
    /// administrador de receber nome ou contagem de outra unidade. O filtro de unidade do master não se aplica aqui
    /// (um ranking de uma unidade só não compara nada); a faixa etária, sim.
    /// PROPOSTA (critério de ordem): mais respostas primeiro; no empate, nome da unidade e, por fim, o id. Unidade
    /// sem resposta no período não aparece.
    /// </summary>
    private static async Task<IReadOnlyList<UnitActivity>?> TopUnitsAsync(
        IAppDbContext db, ICurrentUser user, DashboardQuery query, DateOnly today, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        if (!user.IsInRole(RoleNames.Master))
        {
            return null;
        }

        var rows = await (
                from a in db.QuizAnswers
                where a.AnsweredAt >= start && a.AnsweredAt < end
                join qs in db.QuizSessions on a.SessionId equals qs.Id
                join s in GetDashboard.ScopedStudents(db, user, query with { UnitId = null }, today) on qs.StudentId equals s.Id
                join u in db.Units on s.UnitId equals u.Id
                group s by new { u.Id, u.Name } into g
                select new { g.Key.Id, g.Key.Name, Answers = g.Count(), ActiveStudents = g.Select(x => x.Id).Distinct().Count() })
            .OrderByDescending(r => r.Answers).ThenBy(r => r.Name).ThenBy(r => r.Id)
            .Take(Limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new UnitActivity(
            r.Name, r.Answers, r.ActiveStudents, Math.Round((double)r.Answers / rows[0].Answers, 2, MidpointRounding.AwayFromZero)))];
    }
}
