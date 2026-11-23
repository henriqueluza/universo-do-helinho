using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

/// <summary>Colunas e índices do ADR-0003 §5.1.</summary>
internal sealed class QueueJobConfiguration : IEntityTypeConfiguration<QueueJob>
{
    public void Configure(EntityTypeBuilder<QueueJob> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(60);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Attempts).IsConcurrencyToken();
        b.Property(x => x.LastError).HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.Property(x => x.ExternalId).HasMaxLength(200);
        b.Property(x => x.Feedback).HasMaxLength(30);

        // Busca do próximo lote, por tipo (§6.1).
        b.HasIndex(x => new { x.Type, x.NextAttemptAt }).HasDatabaseName("IX_QueueJobs_Type_NextAttemptAt")
            .HasFilter("\"Status\" IN ('Pending', 'Processing')");
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_QueueJobs_IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        // Limites por conta e situação mostrada nas telas.
        b.HasIndex(x => new { x.Type, x.SubjectId, x.CreatedAt }).HasDatabaseName("IX_QueueJobs_Type_SubjectId_CreatedAt");
        // Localizar o trabalho quando o webhook do provedor chega.
        b.HasIndex(x => x.ExternalId).HasDatabaseName("IX_QueueJobs_ExternalId")
            .HasFilter("\"ExternalId\" IS NOT NULL");
    }
}
