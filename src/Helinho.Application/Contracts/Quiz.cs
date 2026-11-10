namespace Helinho.Application.Contracts;

/// <summary>GET /quiz/random?quiz_type=... (P8: devolve a pergunta corrente da sessão).</summary>
public sealed record QuizRandomRequest(string QuizType);

public sealed record QuizOption(string Id, string Label);

public sealed record QuizQuestionResponse(
    string QuestionId, string QuizType, string Statement, IReadOnlyList<QuizOption> Options,
    int Position, int Total, int TimeLimit);

/// <summary>POST /quiz/{question_id}/answer.</summary>
public sealed record AnswerRequest(string OptionId);

public sealed record AnswerResponse(
    bool IsCorrect, string CorrectOptionId, string CorrectAnswerText, string Explanation,
    int XpGained, int StreakCount, int CurrentXp, int NextLevelXp, bool HasNext);
