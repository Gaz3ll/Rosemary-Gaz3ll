using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOR.Domain.Entities;

namespace SOR.Infrastructure.Persistence.Configurations;

/// <summary>Mapowanie encji <see cref="User"/> — tabela użytkowników.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Login)
            .IsRequired()
            .HasMaxLength(64)
            .UseCollation("NOCASE"); // logowanie bez rozróżniania wielkości liter

        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(160);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(128);
        builder.Property(u => u.PasswordSalt).IsRequired().HasMaxLength(64);
        builder.Property(u => u.ProfessionalLicenseNumber).HasMaxLength(32);
        builder.Property(u => u.Role).HasConversion<int>();

        builder.HasIndex(u => u.Login).IsUnique();

    }
}

/// <summary>Mapowanie encji <see cref="Zone"/> — tabela stref oddziału.</summary>
public sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("Zones");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.Code).IsRequired().HasMaxLength(3);
        builder.Property(z => z.Name).IsRequired().HasMaxLength(120);
        builder.Property(z => z.Kind).HasConversion<int>();
        builder.Property(z => z.Capacity).IsRequired();

        builder.HasIndex(z => z.Code).IsUnique();

        builder.HasMany(z => z.DutyShifts)
            .WithOne()
            .HasForeignKey(d => d.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(z => z.StaffAssignments)
            .WithOne()
            .HasForeignKey(a => a.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}

/// <summary>Mapowanie encji <see cref="DutyRoster"/> — tabela grafików dyżurów.</summary>
public sealed class DutyRosterConfiguration : IEntityTypeConfiguration<DutyRoster>
{
    public void Configure(EntityTypeBuilder<DutyRoster> builder)
    {
        builder.ToTable("DutyRosters");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(120);

        builder.HasMany(r => r.Shifts)
            .WithOne()
            .HasForeignKey("DutyRosterId")
            .OnDelete(DeleteBehavior.Cascade);

    }
}

/// <summary>Mapowanie encji <see cref="DutyShift"/> — tabela wpisów grafiku.</summary>
public sealed class DutyShiftConfiguration : IEntityTypeConfiguration<DutyShift>
{
    public void Configure(EntityTypeBuilder<DutyShift> builder)
    {
        builder.ToTable("DutyShifts");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Notes).HasMaxLength(400);

        builder.HasIndex(s => new { s.UserId, s.StartsAtUtc });
        builder.HasIndex(s => s.ZoneId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nawigacja do przypisania personelu nie jest mapowana relacją bazodanową —
        // jest derywowana z tabeli StaffZoneAssignments na poziomie domeny.
        builder.Ignore(s => s.StaffAssignment);
    }
}

/// <summary>Mapowanie encji <see cref="StaffZoneAssignment"/> — tabela przypisań i historii rotacji.</summary>
public sealed class StaffZoneAssignmentConfiguration : IEntityTypeConfiguration<StaffZoneAssignment>
{
    public void Configure(EntityTypeBuilder<StaffZoneAssignment> builder)
    {
        builder.ToTable("StaffZoneAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).HasConversion<int>();

        // Obiekt wartości ReassignmentReason jest mapowany na kolumny złożone (owned entity type).
        builder.OwnsOne(a => a.Reason, reason =>
        {
            reason.Property(r => r.Code).HasColumnName("ReasonCode").HasConversion<int>();
            reason.Property(r => r.Comment).HasColumnName("ReasonComment").HasMaxLength(500);
            reason.Property(r => r.DeclaredAtUtc).HasColumnName("ReasonDeclaredAtUtc");
        });

        builder.Property(a => a.SupersedesAssignmentId).HasColumnName("SupersedesAssignmentId");

        builder.HasIndex(a => new { a.UserId, a.EffectiveToUtc });
        builder.HasIndex(a => a.ZoneId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Zone>()
            .WithMany()
            .HasForeignKey(a => a.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(a => a.History);
    }
}
