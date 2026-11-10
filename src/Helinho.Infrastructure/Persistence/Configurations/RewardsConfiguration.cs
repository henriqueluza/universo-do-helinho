using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class BadgeConfiguration : IEntityTypeConfiguration<Badge>
{
    public void Configure(EntityTypeBuilder<Badge> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).HasMaxLength(60);
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Visual).HasMaxLength(200);
    }
}

internal sealed class StudentBadgeConfiguration : IEntityTypeConfiguration<StudentBadge>
{
    public void Configure(EntityTypeBuilder<StudentBadge> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.BadgeId }).IsUnique();
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Badge>().WithMany().HasForeignKey(x => x.BadgeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.Type }).IsUnique(); // uma seleção por aba de customização
        b.Property(x => x.ItemId).HasMaxLength(60);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Type).HasMaxLength(40);
        b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}
