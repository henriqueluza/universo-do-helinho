namespace Helinho.Application.Contracts;

/// <summary>GET /profile. Serve a Home e o Perfil. `NextLevelXp` é o limiar do próximo nível.</summary>
public sealed record ProfileResponse(
    string DisplayName, string Avatar, int Level, string LevelName, int CurrentXp, int NextLevelXp);

/// <summary>GET /live-event. Sem evento: `HasEvent` false e demais campos vazios (P5); forma única (regra 1.7).</summary>
public sealed record LiveEventResponse(
    bool HasEvent, string Id, string Title, string Subtitle, string QuizType, DateTime EndsAt);

public enum QuizProgressState
{
    NotStarted,
    InProgress,
    New,
    Locked,
}

public sealed record QuizProgressItem(string QuizType, int Current, int Total, QuizProgressState State);

/// <summary>GET /quizzes/progress.</summary>
public sealed record QuizProgressResponse(IReadOnlyList<QuizProgressItem> Progress);
