using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class QuizSessionConfiguration : IEntityTypeConfiguration<QuizSession>
{
    public void Configure(EntityTypeBuilder<QuizSession> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.QuizType });
        b.Property(x => x.QuizType).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QuizAnswerConfiguration : IEntityTypeConfiguration<QuizAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAnswer> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.SessionId, x.QuestionId }).IsUnique(); // P8: uma resposta por sessão e pergunta
        b.Property(x => x.OptionKey).HasMaxLength(1);
        b.HasOne<QuizSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
