using Helinho.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helinho.Infrastructure.Persistence.Configurations;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.Enrollment).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.UnitId);
        b.Property(x => x.Enrollment).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Rg).HasMaxLength(30);
        b.Property(x => x.Cpf).HasMaxLength(14);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.GuardianName).HasMaxLength(200);
        // Sem índice e sem unicidade: irmãos têm o mesmo responsável (ADR-0003 §9).
        b.Property(x => x.GuardianEmail).HasMaxLength(Helinho.Domain.EmailAddress.MaxLength);
        b.Property(x => x.PreviousGuardianEmail).HasMaxLength(Helinho.Domain.EmailAddress.MaxLength);
        b.Property(x => x.ClassName).HasMaxLength(60);
        b.Property(x => x.Avatar).HasMaxLength(60);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithOne().HasForeignKey<Student>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
