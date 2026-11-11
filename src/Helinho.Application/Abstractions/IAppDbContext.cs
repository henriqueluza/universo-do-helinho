using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Helinho.Application.Abstractions;

/// <summary>Porta de persistência (concessão 1 do ADR-0001): os casos de uso consultam os DbSet direto.</summary>
public interface IAppDbContext
{
    DbSet<Unit> Units { get; }

    DbSet<Student> Students { get; }

    DbSet<Question> Questions { get; }

    DbSet<QuestionOption> QuestionOptions { get; }

    DbSet<QuizSession> QuizSessions { get; }

    DbSet<QuizAnswer> QuizAnswers { get; }

    DbSet<LiveEvent> LiveEvents { get; }

    DbSet<DailyQuest> DailyQuests { get; }

    DbSet<QuestProgress> QuestProgress { get; }

    DbSet<Badge> Badges { get; }

    DbSet<StudentBadge> StudentBadges { get; }

    DbSet<InventoryItem> InventoryItems { get; }

    DbSet<ApiSession> ApiSessions { get; }

    DbSet<PushToken> PushTokens { get; }

    /// <summary>Abre transação que serializa quem usa a mesma chave (lock de aviso do Postgres, liberado no fim da transação).</summary>
    Task<IDbContextTransaction> BeginLockedTransactionAsync(string lockKey, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
