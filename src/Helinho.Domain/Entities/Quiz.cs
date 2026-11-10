namespace Helinho.Domain.Entities;

/// <summary>Sessão de quiz mantida no servidor por aluno e tipo (P8).</summary>
public class QuizSession
{
    private QuizSession()
    {
    }

    public QuizSession(Guid studentId, QuizType quizType, int total, DateTimeOffset startedAt)
    {
        StudentId = Guard.Required(studentId, "Aluno");
        Total = total > 0 ? total : throw new DomainException("A sessão precisa de ao menos uma pergunta.");
        QuizType = quizType;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentId { get; private set; }

    public QuizType QuizType { get; private set; }

    public int Total { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }
}

/// <summary>Resposta única por sessão e pergunta (P8): garante que o XP some uma vez.</summary>
public class QuizAnswer
{
    private QuizAnswer()
    {
    }

    public QuizAnswer(Guid sessionId, Guid questionId, string optionKey, bool isCorrect, int xpGained, DateTimeOffset answeredAt)
    {
        SessionId = sessionId;
        QuestionId = questionId;
        OptionKey = Guard.Required(optionKey, "Alternativa");
        IsCorrect = isCorrect;
        XpGained = xpGained;
        AnsweredAt = answeredAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid SessionId { get; private set; }

    public Guid QuestionId { get; private set; }

    public string OptionKey { get; private set; } = "";

    public bool IsCorrect { get; private set; }

    public int XpGained { get; private set; }

    public DateTimeOffset AnsweredAt { get; private set; }
}
