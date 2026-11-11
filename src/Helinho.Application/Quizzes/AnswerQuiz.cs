using Helinho.Application.Abstractions;
using Helinho.Application.Contracts;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Helinho.Application.Quizzes;

public enum AnswerStatus
{
    Ok,
    StudentNotFound,
    InvalidOption,
    QuestionNotCurrent,
}

public sealed record AnswerResult(AnswerStatus Status, AnswerResponse? Answer = null);

/// <summary>
/// Caso de uso do POST /quiz/{question_id}/answer (P8, P13). Regras — PROPOSTA, ainda não aceitas pela contratante:
/// <list type="bullet">
/// <item>XP só no acerto: <see cref="XpPerCorrect"/> por pergunta; errar não tira XP.</item>
/// <item><c>streak_count</c>: acertos seguidos do aluno, em qualquer sessão; o erro zera. Derivado das respostas gravadas (sem coluna).</item>
/// <item>Progresso das missões: cada resposta soma 1 às missões genéricas ou do tipo da pergunta, até a meta, no dia de Brasília.</item>
/// </list>
/// Idempotência: a resposta e o XP vão numa única gravação; o índice único (sessão, pergunta) faz a requisição
/// concorrente perdedora falhar sem efeito e devolver o resultado da vencedora.
/// </summary>
public static class AnswerQuiz
{
    public const int XpPerCorrect = 10;

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    public static async Task<AnswerResult> HandleAsync(
        IAppDbContext db, ICurrentStudent current, IClock clock, string? questionId, string? optionId,
        CancellationToken cancellationToken)
    {
        var studentId = current.StudentId;
        var now = clock.UtcNow;

        // ignora-filtro: a API do jogo não tem ICurrentUser de painel; o escopo é o próprio aluno do token (Id).
        var student = await db.Students.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.Id == studentId, cancellationToken);
        if (student is null)
        {
            return new AnswerResult(AnswerStatus.StudentNotFound);
        }

        if (!Guid.TryParse(questionId, out var qid))
        {
            return new AnswerResult(AnswerStatus.QuestionNotCurrent);
        }

        var question = await db.Questions.AsNoTracking().Include(q => q.Options)
            .SingleOrDefaultAsync(q => q.Id == qid, cancellationToken);
        if (question is null)
        {
            return new AnswerResult(AnswerStatus.QuestionNotCurrent);
        }

        var session = await db.QuizSessions
            .Where(s => s.StudentId == studentId && s.QuizType == question.QuizType && s.CompletedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var existing = await FindAnswerAsync(db, studentId, qid, session?.Id, cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(db, studentId, question, existing, cancellationToken);
        }

        var band = student.AgeBandAt(DateOnly.FromDateTime(now.UtcDateTime));
        var answeredCount = session is null ? 0 : await db.QuizAnswers.CountAsync(a => a.SessionId == session.Id, cancellationToken);
        var next = session is not null && answeredCount < session.Total
            ? await GetRandomQuiz.NextAsync(db, GetRandomQuiz.Eligible(db, question.QuizType, band), session.Id, cancellationToken)
            : null;
        if (session is null || next?.Id != qid)
        {
            // Corrida: uma requisição igual pode ter gravado entre a checagem acima e a pergunta corrente.
            existing = await FindAnswerAsync(db, studentId, qid, session?.Id, cancellationToken);
            return existing is not null
                ? await ReplayAsync(db, studentId, question, existing, cancellationToken)
                : new AnswerResult(AnswerStatus.QuestionNotCurrent);
        }

        var chosen = question.Options.FirstOrDefault(o => string.Equals(o.Key, optionId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
        {
            return new AnswerResult(AnswerStatus.InvalidOption);
        }

        var isCorrect = chosen.IsCorrect;
        var xpGained = isCorrect ? XpPerCorrect : 0;
        var streak = isCorrect ? await PriorStreakAsync(db, studentId, now, cancellationToken) + 1 : 0;
        var hasNext = answeredCount + 1 < session.Total;

        db.QuizAnswers.Add(new QuizAnswer(session.Id, qid, chosen.Key, isCorrect, xpGained, now));
        if (!hasNext)
        {
            session.Complete(now);
        }

        await AdvanceQuestsAsync(db, studentId, question.QuizType, now, cancellationToken);

        int xp;
        try
        {
            // Resposta, missões e XP numa transação; o XP é incremento atômico no banco (sem ler-modificar-gravar),
            // para que respostas simultâneas do mesmo aluno em sessões diferentes não percam XP.
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (xpGained > 0)
            {
                // ignora-filtro: escopo do aluno do token (Id).
                await db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Xp, s => s.Xp + xpGained), cancellationToken);
            }

            // ignora-filtro: escopo do aluno do token (Id); lê o XP já atualizado na transação.
            xp = await db.Students.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == studentId).Select(s => s.Xp).SingleAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Requisição concorrente da mesma resposta: a outra venceu o índice único; a transação foi desfeita.
            var winner = await FindAnswerAsync(db, studentId, qid, session.Id, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return await ReplayAsync(db, studentId, question, winner, cancellationToken);
        }

        return Ok(question, isCorrect, xpGained, streak, xp, hasNext);
    }

    /// <summary>Resposta já gravada para a pergunta: na sessão aberta ou, se ela já fechou, na mais recente do aluno.</summary>
    private static async Task<QuizAnswer?> FindAnswerAsync(
        IAppDbContext db, Guid studentId, Guid questionId, Guid? openSessionId, CancellationToken cancellationToken)
    {
        if (openSessionId is { } open)
        {
            return await db.QuizAnswers.AsNoTracking()
                .FirstOrDefaultAsync(a => a.SessionId == open && a.QuestionId == questionId, cancellationToken);
        }

        return await db.QuizAnswers.AsNoTracking()
            .Where(a => a.QuestionId == questionId && db.QuizSessions.Any(s => s.Id == a.SessionId && s.StudentId == studentId))
            .OrderByDescending(a => a.AnsweredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<AnswerResult> ReplayAsync(
        IAppDbContext db, Guid studentId, Question question, QuizAnswer answer, CancellationToken cancellationToken)
    {
        // ignora-filtro: escopo do aluno do token (Id); lê o XP atual do banco, não a cópia rastreada (pode estar defasada).
        var xp = await db.Students.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == studentId).Select(s => s.Xp).SingleAsync(cancellationToken);
        var total = await db.QuizSessions.AsNoTracking()
            .Where(s => s.Id == answer.SessionId).Select(s => s.Total).SingleAsync(cancellationToken);
        var position = await db.QuizAnswers.AsNoTracking()
            .CountAsync(a => a.SessionId == answer.SessionId && a.AnsweredAt <= answer.AnsweredAt, cancellationToken);
        var streak = answer.IsCorrect
            ? await PriorStreakAsync(db, studentId, answer.AnsweredAt, cancellationToken) + 1
            : 0;
        return Ok(question, answer.IsCorrect, answer.XpGained, streak, xp, position < total);
    }

    /// <summary>Acertos seguidos imediatamente antes do instante dado, em todas as sessões do aluno.</summary>
    private static async Task<int> PriorStreakAsync(
        IAppDbContext db, Guid studentId, DateTimeOffset before, CancellationToken cancellationToken)
    {
        var history = db.QuizAnswers.AsNoTracking()
            .Where(a => a.AnsweredAt < before && db.QuizSessions.Any(s => s.Id == a.SessionId && s.StudentId == studentId));
        var lastWrong = await history.Where(a => !a.IsCorrect)
            .Select(a => (DateTimeOffset?)a.AnsweredAt).MaxAsync(cancellationToken);
        return await history.CountAsync(a => lastWrong == null || a.AnsweredAt > lastWrong, cancellationToken);
    }

    private static async Task AdvanceQuestsAsync(
        IAppDbContext db, Guid studentId, QuizType type, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(now.ToOffset(BrasiliaOffset).DateTime);
        var quests = await db.DailyQuests.Where(q => q.QuizType == null || q.QuizType == type).ToListAsync(cancellationToken);
        var progress = await db.QuestProgress.Where(p => p.StudentId == studentId && p.Day == day).ToListAsync(cancellationToken);
        foreach (var quest in quests)
        {
            var row = progress.FirstOrDefault(p => p.QuestId == quest.Id);
            if (row is null)
            {
                row = new QuestProgress(studentId, quest.Id, day, 0);
                db.QuestProgress.Add(row);
            }

            row.Advance(quest.Target);
        }
    }

    private static AnswerResult Ok(Question question, bool isCorrect, int xpGained, int streak, int xp, bool hasNext)
    {
        var correct = question.Options.Single(o => o.IsCorrect);
        return new AnswerResult(
            AnswerStatus.Ok,
            new AnswerResponse(
                isCorrect, correct.Key, correct.Label, question.Explanation, xpGained, streak, xp,
                LevelPolicy.NextLevelXp(xp), hasNext));
    }
}
