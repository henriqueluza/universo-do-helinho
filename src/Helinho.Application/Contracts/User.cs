namespace Helinho.Application.Contracts;

public sealed record PerformanceItem(string QuizType, double Percentage);

/// <summary>GET /user/stats.</summary>
public sealed record UserStatsResponse(
    IReadOnlyList<PerformanceItem> Performance, int AnsweredQuestions, double CorrectAverageQuestions);

public sealed record BadgeItem(string Id, string Title, bool Unlocked, string Visual);

/// <summary>GET /user/badges?page=1 (P11). `page` é query, sem DTO de requisição.</summary>
public sealed record BadgesResponse(IReadOnlyList<BadgeItem> Badges, int Page, int TotalPages);

public sealed record InventoryItem(string Id, string Name, string Type);

/// <summary>Corpo do POST /user/inventory (mesmo formato do GET).</summary>
public sealed record InventoryRequest(IReadOnlyList<InventoryItem> Selected);

/// <summary>GET /user/inventory e resposta do POST (P10).</summary>
public sealed record InventoryResponse(IReadOnlyList<InventoryItem> Selected);
