using Helinho.Application.Abstractions;
using Helinho.Domain;
using Helinho.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Helinho.Infrastructure.Persistence;

/// <summary>Contexto único do sistema: Identity + entidades do domínio.</summary>
public partial class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext, IDataProtectionKeyContext
{
    // Propriedades do contexto: o EF as transforma em parâmetros do filtro global.
    private bool SeesAllUnits => currentUser.IsInRole(RoleNames.Master);

    private Guid? CurrentUnitId => currentUser.UnitId;

    public async Task<IDbContextTransaction> BeginLockedTransactionAsync(string lockKey, CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<Student> Students => Set<Student>();

    public DbSet<Question> Questions => Set<Question>();

    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();

    public DbSet<QuizSession> QuizSessions => Set<QuizSession>();

    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();

    public DbSet<LiveEvent> LiveEvents => Set<LiveEvent>();

    public DbSet<DailyQuest> DailyQuests => Set<DailyQuest>();

    public DbSet<QuestProgress> QuestProgress => Set<QuestProgress>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<StudentBadge> StudentBadges => Set<StudentBadge>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<ApiSession> ApiSessions => Set<ApiSession>();

    public DbSet<PushToken> PushTokens => Set<PushToken>();

    /// <summary>Chaves do Data Protection (ADR-0002 §9). Infraestrutura: fora de <see cref="IAppDbContext"/>, de DTOs e de rotas.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Fila persistente (ADR-0003 §5). Infraestrutura: fora de <see cref="IAppDbContext"/>, de DTOs e de rotas.</summary>
    public DbSet<QueueJob> QueueJobs => Set<QueueJob>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // T1: administrador só enxerga a própria unidade; master enxerga todas; anônimo não enxerga nenhuma.
        builder.Entity<Student>().HasQueryFilter(s => SeesAllUnits || s.UnitId == CurrentUnitId);
    }
}
