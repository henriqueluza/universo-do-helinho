namespace Helinho.Application.Contracts;

public sealed record QuizOption(string Id, string Label);

/// <summary>GET /quiz/random?quiz_type=... (P8: pergunta corrente da sessão). `quiz_type` é query, sem DTO de requisição.</summary>
public sealed record QuizQuestionResponse(
    string QuestionId, string QuizType, string Statement, IReadOnlyList<QuizOption> Options,
    int Position, int Total, int TimeLimit);

/// <summary>POST /quiz/{question_id}/answer.</summary>
public sealed record AnswerRequest(string OptionId);

public sealed record AnswerResponse(
    bool IsCorrect, string CorrectOptionId, string CorrectAnswerText, string Explanation,
    int XpGained, int StreakCount, int CurrentXp, int NextLevelXp, bool HasNext);
