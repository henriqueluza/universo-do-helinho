using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
    }
}
