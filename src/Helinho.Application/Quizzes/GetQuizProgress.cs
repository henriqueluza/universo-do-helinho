using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quizzes;

/// <summary>
/// Caso de uso do GET /quizzes/progress. Regras de `state` — PROPOSTA, ainda não aceitas pela contratante
/// (o desbloqueio definitivo está fora de escopo):
/// <list type="bullet">
/// <item><c>in_progress</c>: há sessão aberta no tipo; <c>current</c> = respostas dadas, <c>total</c> = tamanho da sessão.</item>
/// <item><c>locked</c>: sem sessão aberta e o nível do aluno é menor que o exigido pelo tipo (Diversidade exige nível 2).</item>
/// <item><c>not_started</c>: sem sessão aberta, tipo liberado, e o aluno já concluiu uma sessão nele.</item>
/// <item><c>new</c>: sem sessão aberta, tipo liberado, nunca jogado.</item>
/// </list>
/// Sem sessão aberta: <c>current</c> 0 e <c>total</c> 10 (tamanho padrão da sessão).
/// </summary>
public static class GetQuizProgress
{
    public const int DefaultTotal = 10;

    private const int DiversidadeUnlockLevel = 2;

    public static async Task<QuizProgressResponse?> HandleAsync(
        IAppDbContext db, ICurrentStudent current, CancellationToken cancellationToken)
    {
        var studentId = current.StudentId;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var xp = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == studentId)
            .Select(s => (int?)s.Xp)
            .SingleOrDefaultAsync(cancellationToken);
        if (xp is null)
        {
            return null;
        }

        var sessions = await db.QuizSessions
            .Where(s => s.StudentId == studentId)
            .Select(s => new
            {
                s.QuizType,
                s.Total,
                s.StartedAt,
                Open = s.CompletedAt == null,
                Answered = db.QuizAnswers.Count(a => a.SessionId == s.Id),
            })
            .ToListAsync(cancellationToken);

        var level = LevelPolicy.LevelFor(xp.Value);
        var items = Enum.GetValues<QuizType>().Select(type =>
        {
            var own = sessions.Where(s => s.QuizType == type).ToList();
            var open = own.Where(s => s.Open).OrderByDescending(s => s.StartedAt).FirstOrDefault();
            var name = type.ToString().ToLowerInvariant();
            if (open is not null)
            {
                return new QuizProgressItem(name, Math.Min(open.Answered, open.Total), open.Total, QuizProgressState.InProgress);
            }

            var state = type == QuizType.Diversidade && level < DiversidadeUnlockLevel ? QuizProgressState.Locked
                : own.Count > 0 ? QuizProgressState.NotStarted
                : QuizProgressState.New;
            return new QuizProgressItem(name, 0, DefaultTotal, state);
        }).ToList();

        return new QuizProgressResponse(items);
    }
}
