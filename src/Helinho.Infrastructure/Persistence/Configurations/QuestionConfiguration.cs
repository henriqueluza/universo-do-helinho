using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.QuizType, x.AgeBand, x.Status });
        b.Property(x => x.QuizType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AgeBand).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Statement).HasMaxLength(500);
        b.Property(x => x.Explanation).HasMaxLength(1000);
        // Proveniência (ADR-0004 §9.2). As linhas que já existiam recebem Team pelo padrão da coluna.
        b.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(QuestionOrigin.Team);
        b.Property(x => x.PromptVersion).HasMaxLength(Question.PromptVersionMax);
        b.HasOne<AiGenerationJob>().WithMany().HasForeignKey(x => x.AiJobId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class QuestionOptionConfiguration : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.QuestionId, x.Key }).IsUnique();
        b.Property(x => x.Key).HasMaxLength(1);
        b.Property(x => x.Label).HasMaxLength(300);
    }
}
