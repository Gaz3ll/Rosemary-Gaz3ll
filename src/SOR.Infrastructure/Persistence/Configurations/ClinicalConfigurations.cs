using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOR.Domain.Entities;

namespace SOR.Infrastructure.Persistence.Configurations;

/// <summary>Mapowanie encji <see cref="Patient"/> — tabela kart pacjentów.</summary>
public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Pesel).IsRequired().HasMaxLength(11);
        builder.Property(p => p.FirstName).IsRequired().HasMaxLength(64);
        builder.Property(p => p.LastName).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Complaint).HasMaxLength(1000);
        builder.Property(p => p.State).HasConversion<int>();
        builder.Property(p => p.Gender).HasConversion<int>();
        builder.Property(p => p.LockedBy).HasMaxLength(64);

        // ICD-10 jako owned entity type (kolumny rozłożone, brak kolumny klucza obcego).
        builder.OwnsOne(p => p.Diagnosis, diagnosis =>
        {
            diagnosis.Property(d => d.Value).HasColumnName("Icd10Code").HasMaxLength(8);
        });

        builder.Navigation(p => p.Diagnosis).IsRequired(false);

        builder.HasIndex(p => p.Pesel).IsUnique();
        builder.HasIndex(p => new { p.ZoneId, p.State });

        builder.HasOne<Zone>()
            .WithMany()
            .HasForeignKey(p => p.ZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(p => p.OpenOrders);
        builder.Ignore(p => p.FullName);
    }
}

/// <summary>Mapowanie encji <see cref="TriageAssessment"/> — tabela ocen segregacji medycznej.</summary>
public sealed class TriageAssessmentConfiguration : IEntityTypeConfiguration<TriageAssessment>
{
    public void Configure(EntityTypeBuilder<TriageAssessment> builder)
    {
        builder.ToTable("TriageAssessments");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Category).HasConversion<int>();
        builder.Property(t => t.ClinicalJustification).IsRequired().HasMaxLength(1000);
        builder.Property(t => t.VitalSignsSummary).HasMaxLength(400);

        builder.HasIndex(t => new { t.PatientId, t.AssessedAtUtc });

        builder.HasOne<Patient>()
            .WithMany(p => p.TriageHistory)
            .HasForeignKey(t => t.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.AssessedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(t => t.Policy);
        builder.Ignore(t => t.MaxWaitTime);
        builder.Ignore(t => t.LoadWeight);
    }
}

/// <summary>Mapowanie encji <see cref="MedicalOrder"/> — tabela zleceń lekarskich.</summary>
public sealed class MedicalOrderConfiguration : IEntityTypeConfiguration<MedicalOrder>
{
    public void Configure(EntityTypeBuilder<MedicalOrder> builder)
    {
        builder.ToTable("MedicalOrders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type).HasConversion<int>();
        builder.Property(o => o.State).HasConversion<int>();
        builder.Property(o => o.OrderedByRole).HasConversion<int>();
        builder.Property(o => o.Description).IsRequired().HasMaxLength(1000);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        builder.HasIndex(o => new { o.PatientId, o.State });

        builder.HasOne<Patient>()
            .WithMany(p => p.Orders)
            .HasForeignKey(o => o.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.OrderedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(o => o.IsBlocking);
    }
}

/// <summary>Mapowanie encji <see cref="ZoneTransfer"/> — tabela historii przeniesień pacjentów.</summary>
public sealed class ZoneTransferConfiguration : IEntityTypeConfiguration<ZoneTransfer>
{
    public void Configure(EntityTypeBuilder<ZoneTransfer> builder)
    {
        builder.ToTable("ZoneTransfers");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Reason).IsRequired().HasMaxLength(500);

        builder.HasIndex(t => new { t.PatientId, t.TransferredAtUtc });

        builder.HasOne<Patient>()
            .WithMany(p => p.Transfers)
            .HasForeignKey(t => t.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Zone>()
            .WithMany()
            .HasForeignKey(t => t.ToZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(t => t.IsInitialAssignment);
    }
}

/// <summary>Mapowanie encji <see cref="AuditLogEntry"/> — tabela dziennika audytu (append-only).</summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLogEntries");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActionType).HasConversion<int>();
        builder.Property(a => a.ActorLogin).IsRequired().HasMaxLength(64);
        builder.Property(a => a.EntityType).HasMaxLength(64);
        builder.Property(a => a.Details).IsRequired().HasMaxLength(2000);
        builder.Property(a => a.Success).IsRequired().HasColumnName("Success");

        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.ActionType);

        builder.Ignore(a => a.IsFailure);
    }
}
