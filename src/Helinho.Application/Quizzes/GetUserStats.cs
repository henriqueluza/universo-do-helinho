using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quizzes;

/// <summary>
/// Caso de uso do GET /user/stats. <c>performance[].percentage</c> = acertos / respostas do aluno no tipo (0 sem respostas).
/// <c>answered_questions</c> e <c>correct_average_questions</c> (acertos / respostas) saem de todas as respostas do aluno,
/// não da média das categorias. O fator definitivo do cálculo está fora de escopo. Ordem dos tipos: a do exemplo do contrato.
/// </summary>
public static class GetUserStats
{
    private static readonly QuizType[] ContractOrder =
        [QuizType.Conhecimento, QuizType.Ambiental, QuizType.Esportivo, QuizType.Diversidade, QuizType.Cultural];

    public static async Task<UserStatsResponse?> HandleAsync(
        IAppDbContext db, ICurrentStudent current, CancellationToken cancellationToken)
    {
        var studentId = current.StudentId;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var exists = await db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == studentId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var perType = await db.QuizAnswers
            .Join(db.QuizSessions.Where(s => s.StudentId == studentId), a => a.SessionId, s => s.Id,
                (a, s) => new { s.QuizType, a.IsCorrect })
            .GroupBy(x => x.QuizType)
            .Select(g => new { QuizType = g.Key, Answered = g.Count(), Correct = g.Count(x => x.IsCorrect) })
            .ToListAsync(cancellationToken);

        var performance = ContractOrder
            .Select(type => perType.FirstOrDefault(p => p.QuizType == type))
            .Select((p, i) => new PerformanceItem(
                ContractOrder[i].ToString().ToLowerInvariant(), Ratio(p?.Correct ?? 0, p?.Answered ?? 0)))
            .ToList();

        var answered = perType.Sum(p => p.Answered);
        return new UserStatsResponse(performance, answered, Ratio(perType.Sum(p => p.Correct), answered));
    }

    private static double Ratio(int correct, int answered) => answered == 0 ? 0 : (double)correct / answered;
}
