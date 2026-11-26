using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

/// <summary>Colunas do ADR-0004 §9.2. Quem pediu é só o identificador do usuário, sem chave estrangeira (como <c>QueueJobs.SubjectId</c>).</summary>
internal sealed class AiGenerationJobConfiguration : IEntityTypeConfiguration<AiGenerationJob>
{
    public void Configure(EntityTypeBuilder<AiGenerationJob> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.QuizType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AgeBand).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Subject).HasMaxLength(AiGenerationJob.SubjectMax);
        b.Property(x => x.FailureReason).HasMaxLength(AiGenerationJob.FailureReasonMax);
        b.Property(x => x.Provider).HasMaxLength(AiGenerationJob.ProviderMax);
        b.Property(x => x.Model).HasMaxLength(AiGenerationJob.ModelMax);
        b.Property(x => x.PromptVersion).HasMaxLength(AiGenerationJob.PromptVersionMax);

        // Cota por pessoa e teto global: soma de RequestedCount na janela de 24 horas (§13).
        b.HasIndex(x => new { x.RequestedByUserId, x.CreatedAt });
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class AiQuestionSuggestionConfiguration : IEntityTypeConfiguration<AiQuestionSuggestion>
{
    public void Configure(EntityTypeBuilder<AiQuestionSuggestion> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Statement).HasMaxLength(500);
        b.Property(x => x.OptionA).HasMaxLength(300);
        b.Property(x => x.OptionB).HasMaxLength(300);
        b.Property(x => x.OptionC).HasMaxLength(300);
        b.Property(x => x.OptionD).HasMaxLength(300);
        b.Property(x => x.Explanation).HasMaxLength(1000);
        // Token de concorrência (§9.3): a decisão só vale a partir de Pending; a segunda gravação simultânea é recusada.
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        b.Ignore(x => x.Options);

        b.HasOne<AiGenerationJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.JobId, x.Position }).IsUnique();
        // "N sugestões esperando revisão" e a deduplicação contra as sugestões ainda guardadas (§8).
        b.HasIndex(x => x.Status);
    }
}
