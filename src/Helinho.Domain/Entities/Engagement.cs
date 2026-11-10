namespace Helinho.Domain.Entities;

/// <summary>Evento destaca um único tipo de quiz existente.</summary>
public class LiveEvent(string title, string subtitle, QuizType quizType, DateTimeOffset endsAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Title { get; private set; } = Guard.Required(title, "Título");

    public string Subtitle { get; private set; } = subtitle ?? "";

    public QuizType QuizType { get; private set; } = quizType;

    public DateTimeOffset EndsAt { get; private set; } = endsAt;
}

/// <summary>Missão diária; <see cref="QuizType"/> nulo é missão genérica (P6).</summary>
public class DailyQuest(string title, int target, int rewardXp, QuizType? quizType)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Title { get; private set; } = Guard.Required(title, "Título");

    public int Target { get; private set; } = target > 0 ? target : throw new DomainException("Meta deve ser positiva.");

    public int RewardXp { get; private set; } = rewardXp;

    public QuizType? QuizType { get; private set; } = quizType;
}

public class QuestProgress(Guid studentId, Guid questId, DateOnly day, int current)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentId { get; private set; } = studentId;

    public Guid QuestId { get; private set; } = questId;

    public DateOnly Day { get; private set; } = day;

    public int Current { get; private set; } = current;

    /// <summary>Soma uma resposta ao progresso, sem passar da meta.</summary>
    public void Advance(int target) => Current = Math.Min(target, Current + 1);
}

public class Badge(string code, string title, string visual)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Identificador do contrato (`id`), ex.: primeiro-passo.</summary>
    public string Code { get; private set; } = Guard.Required(code, "Código");

    public string Title { get; private set; } = Guard.Required(title, "Título");

    public string Visual { get; private set; } = visual ?? "";
}

public class StudentBadge(Guid studentId, Guid badgeId, DateTimeOffset unlockedAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentId { get; private set; } = studentId;

    public Guid BadgeId { get; private set; } = badgeId;

    public DateTimeOffset UnlockedAt { get; private set; } = unlockedAt;
}

/// <summary>Item de customização escolhido pelo aluno (`selected` do inventário).</summary>
public class InventoryItem(Guid studentId, string itemId, string name, string type)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentId { get; private set; } = studentId;

    public string ItemId { get; private set; } = Guard.Required(itemId, "Item");

    public string Name { get; private set; } = Guard.Required(name, "Nome");

    public string Type { get; private set; } = Guard.Required(type, "Tipo");
}
