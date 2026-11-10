using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class ApiSessionConfiguration : IEntityTypeConfiguration<ApiSession>
{
    public void Configure(EntityTypeBuilder<ApiSession> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.TokenHash).HasMaxLength(128);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PushTokenConfiguration : IEntityTypeConfiguration<PushToken>
{
    public void Configure(EntityTypeBuilder<PushToken> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.Token }).IsUnique();
        b.Property(x => x.Token).HasMaxLength(512);
        b.Property(x => x.Platform).HasMaxLength(20);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
