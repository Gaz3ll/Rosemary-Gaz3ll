using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence.Configurations;

/// <summary>Mapowanie encji <see cref="Medication"/> — katalog leków SOR.</summary>
public sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("Medications");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Code).IsRequired().HasMaxLength(32);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Form).IsRequired().HasMaxLength(120);
        builder.Property(m => m.Strength).IsRequired().HasMaxLength(80);
        builder.Property(m => m.TypicalDose).IsRequired().HasMaxLength(200);
        builder.Property(m => m.MaxDailyDose).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Contraindications).HasMaxLength(1000);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.Property(m => m.Category).HasConversion<int>();
        builder.Property(m => m.Route).HasConversion<int>();
        builder.Property(m => m.Safety).HasConversion<int>();

        builder.HasIndex(m => m.Code).IsUnique();
        builder.HasIndex(m => m.Category);

        builder.Ignore(m => m.DisplayName);
    }
}

/// <summary>Mapowanie encji <see cref="MedicationAdministration"/> — rejestr podanych leków.</summary>
public sealed class MedicationAdministrationConfiguration : IEntityTypeConfiguration<MedicationAdministration>
{
    public void Configure(EntityTypeBuilder<MedicationAdministration> builder)
    {
        builder.ToTable("MedicationAdministrations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Dose).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.Route).HasConversion<int>();

        builder.HasIndex(a => new { a.PatientId, a.AdministeredAtUtc });
        builder.HasIndex(a => a.MedicationId);

        builder.HasOne<Patient>()
            .WithMany(p => p.Administrations)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Medication>()
            .WithMany()
            .HasForeignKey(a => a.MedicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.AdministeredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MedicalOrder>()
            .WithMany()
            .HasForeignKey(a => a.MedicalOrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>Mapowanie encji <see cref="Icd10CatalogEntry"/> — katalog kodów ICD-10.</summary>
public sealed class Icd10CatalogEntryConfiguration : IEntityTypeConfiguration<Icd10CatalogEntry>
{
    public void Configure(EntityTypeBuilder<Icd10CatalogEntry> builder)
    {
        builder.ToTable("Icd10Catalog");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).IsRequired().HasMaxLength(8);
        builder.Property(e => e.Description).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Chapter).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Category).HasMaxLength(200);

        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.IsEmergencyRelevant);
    }
}

/// <summary>Mapowanie encji <see cref="MedicalBundle"/> — pakiety medyczne.</summary>
public sealed class MedicalBundleConfiguration : IEntityTypeConfiguration<MedicalBundle>
{
    public void Configure(EntityTypeBuilder<MedicalBundle> builder)
    {
        builder.ToTable("MedicalBundles");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code).IsRequired().HasMaxLength(32);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Indication).IsRequired().HasMaxLength(1000);
        builder.Property(b => b.Chapter).IsRequired().HasMaxLength(120);

        builder.HasIndex(b => b.Code).IsUnique();

        builder.HasMany(b => b.Items)
            .WithOne()
            .HasForeignKey(i => i.BundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapowanie encji <see cref="MedicalBundleItem"/> — pozycje pakietów medycznych.</summary>
public sealed class MedicalBundleItemConfiguration : IEntityTypeConfiguration<MedicalBundleItem>
{
    public void Configure(EntityTypeBuilder<MedicalBundleItem> builder)
    {
        builder.ToTable("MedicalBundleItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).IsRequired().HasMaxLength(500);
        builder.Property(i => i.Dose).HasMaxLength(200);
        builder.Property(i => i.OrderType).HasConversion<int>();
        builder.Property(i => i.Route).HasConversion<int>();

        builder.HasIndex(i => new { i.BundleId, i.Sequence }).IsUnique();

        builder.HasOne<Medication>()
            .WithMany()
            .HasForeignKey(i => i.MedicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(i => i.RequiresOrder);
    }
}
