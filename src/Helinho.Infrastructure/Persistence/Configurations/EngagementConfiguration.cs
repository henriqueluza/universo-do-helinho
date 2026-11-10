using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class LiveEventConfiguration : IEntityTypeConfiguration<LiveEvent>
{
    public void Configure(EntityTypeBuilder<LiveEvent> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Subtitle).HasMaxLength(200);
        b.Property(x => x.QuizType).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class DailyQuestConfiguration : IEntityTypeConfiguration<DailyQuest>
{
    public void Configure(EntityTypeBuilder<DailyQuest> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.QuizType).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class QuestProgressConfiguration : IEntityTypeConfiguration<QuestProgress>
{
    public void Configure(EntityTypeBuilder<QuestProgress> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.QuestId, x.Day }).IsUnique();
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DailyQuest>().WithMany().HasForeignKey(x => x.QuestId).OnDelete(DeleteBehavior.Cascade);
    }
}
