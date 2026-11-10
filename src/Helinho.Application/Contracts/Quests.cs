namespace Helinho.Application.Contracts;

/// <summary>`QuizType` vazio ("") é missão genérica (P6).</summary>
public sealed record QuestItem(string Id, string Title, int Current, int Total, int RewardXp, string QuizType);

public sealed record DailyQuestsResponse(DateTime RenewsAt, IReadOnlyList<QuestItem> Quests);

public sealed record DailyBonusResponse(int RewardXp, int RequiredQuests);
