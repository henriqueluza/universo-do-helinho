using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quizzes;

public enum RandomQuizStatus
{
    Ok,
    InvalidQuizType,
    StudentNotFound,
    NoQuestions,
}

public sealed record RandomQuizResult(RandomQuizStatus Status, QuizQuestionResponse? Question = null);

/// <summary>
/// Caso de uso do GET /quiz/random (P8). O servidor guarda a sessão por aluno e tipo: devolve sempre a pergunta
/// corrente (a primeira aprovada, do tipo e da faixa do aluno, ainda sem resposta na sessão, em ordem estável de Id)
/// e só avança quando ela é respondida. Sessão esgotada é encerrada e a próxima chamada abre outra.
/// </summary>
public static class GetRandomQuiz
{
    public const int TimeLimitSeconds = 30;

    public static async Task<RandomQuizResult> HandleAsync(
        IAppDbContext db, ICurrentStudent current, IClock clock, string? quizType, CancellationToken cancellationToken)
    {
        if (!TryParse(quizType, out var type))
        {
            return new RandomQuizResult(RandomQuizStatus.InvalidQuizType);
        }

        var studentId = current.StudentId;
        var now = clock.UtcNow;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var birthDate = await db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == studentId)
            .Select(s => (DateOnly?)s.BirthDate)
            .SingleOrDefaultAsync(cancellationToken);
        if (birthDate is null)
        {
            return new RandomQuizResult(RandomQuizStatus.StudentNotFound);
        }

        var band = AgeBands.FromBirthDate(birthDate.Value, DateOnly.FromDateTime(now.UtcDateTime));
        var eligible = Eligible(db, type, band);

        var session = await db.QuizSessions
            .Where(s => s.StudentId == studentId && s.QuizType == type && s.CompletedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is not null)
        {
            var answeredCount = await db.QuizAnswers.CountAsync(a => a.SessionId == session.Id, cancellationToken);
            var question = answeredCount < session.Total ? await NextAsync(db, eligible, session.Id, cancellationToken) : null;
            if (question is not null)
            {
                return Ok(question, type, answeredCount + 1, session.Total);
            }

            session.Complete(now);
            session = null;
        }

        var pool = await eligible.CountAsync(cancellationToken);
        if (pool == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return new RandomQuizResult(RandomQuizStatus.NoQuestions);
        }

        session = new QuizSession(studentId, type, Math.Min(GetQuizProgress.DefaultTotal, pool), now);
        db.QuizSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        var first = await NextAsync(db, eligible, session.Id, cancellationToken);
        return first is null
            ? new RandomQuizResult(RandomQuizStatus.NoQuestions)
            : Ok(first, type, 1, session.Total);
    }

    internal static IQueryable<Question> Eligible(IAppDbContext db, QuizType type, AgeBand band) =>
        db.Questions.Where(q => q.Status == QuestionStatus.Approved && q.QuizType == type && q.AgeBand == band);

    internal static Task<Question?> NextAsync(
        IAppDbContext db, IQueryable<Question> eligible, Guid sessionId, CancellationToken cancellationToken) =>
        eligible
            .Where(q => !db.QuizAnswers.Any(a => a.SessionId == sessionId && a.QuestionId == q.Id))
            .OrderBy(q => q.Id)
            .Include(q => q.Options)
            .FirstOrDefaultAsync(cancellationToken);

    private static RandomQuizResult Ok(Question question, QuizType type, int position, int total) =>
        new(
            RandomQuizStatus.Ok,
            new QuizQuestionResponse(
                question.Id.ToString(),
                type.ToString().ToLowerInvariant(),
                question.Statement,
                question.Options.OrderBy(o => o.Key).Select(o => new QuizOption(o.Key, o.Label)).ToList(),
                position,
                total,
                TimeLimitSeconds));

    private static bool TryParse(string? value, out QuizType type)
    {
        type = default;
        return !string.IsNullOrWhiteSpace(value)
            && Enum.GetValues<QuizType>().Any(t => string.Equals(t.ToString(), value, StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(value, ignoreCase: true, out type);
    }
}
