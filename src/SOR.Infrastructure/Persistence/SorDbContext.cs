using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SOR.Domain.Entities;
using SOR.Domain.Common;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Kontekst trwałości (DbContext) mapujący model domenowy na relacyjną bazę SQLite.
/// Konfiguracja jest rozdzielona na klasy <c>IEntityTypeConfiguration&lt;T&gt;</c> (konfiguracja per encja),
/// co utrzymuje klasę kontekstu zwartej i łatwej w utrzymaniu.
/// </summary>
public sealed class SorDbContext : DbContext
{
    public SorDbContext(DbContextOptions<SorDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<DutyRoster> DutyRosters => Set<DutyRoster>();

    public DbSet<DutyShift> DutyShifts => Set<DutyShift>();

    public DbSet<StaffZoneAssignment> StaffZoneAssignments => Set<StaffZoneAssignment>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();

    public DbSet<MedicalOrder> MedicalOrders => Set<MedicalOrder>();

    public DbSet<ZoneTransfer> ZoneTransfers => Set<ZoneTransfer>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<Medication> Medications => Set<Medication>();

    public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();

    public DbSet<Icd10CatalogEntry> Icd10CatalogEntries => Set<Icd10CatalogEntry>();

    public DbSet<MedicalBundle> MedicalBundles => Set<MedicalBundle>();

    public DbSet<MedicalBundleItem> MedicalBundleItems => Set<MedicalBundleItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SorDbContext).Assembly);

        // Globalna konfiguracja właściwa dla wszystkich encji (konwencja konfiguracyjna).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.IsSubclassOf(typeof(Entity<Guid>)))
            {
                // Token współbieżności optymistycznej. SQLite nie posiada natywnego mechanizmu
                // generowania wartości wersji, dlatego jest ona nadawana w metodzie StampConcurrencyTokens
                // bezpośrednio przed każdym zapisem. Jawne ValueGeneratedNever() jest konieczne, ponieważ
                // konwencja EF Core rozpoznaje nazwę RowVersion jako wartość generowaną przez magazyn.
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(Entity<Guid>.RowVersion))
                    .IsConcurrencyToken()
                    .ValueGeneratedNever();
            }
        }
    }

    /// <summary>
    /// Zapis zmian z nadaniem nowych tokenów współbieżności. Wywołanie <see cref="StampConcurrencyTokens"/>
    /// przed podstawową implementacją gwarantuje, że każda modyfikacja trafia do bazy z nową wersją,
    /// a warunek <c>WHERE</c> używa wersji oryginalnej odczytanej z bazy.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Nadaje nowe, unikalne tokeny wersji encjom dodawanym i modyfikowanym.</summary>
    private void StampConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified) ||
                entry.Entity is not Entity<Guid> entity)
            {
                continue;
            }

            entity.StampRowVersion(Guid.NewGuid().ToByteArray());

            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(Entity<Guid>.RowVersion)).IsModified = true;
            }
        }
    }

    /// <summary>
    /// Konwencje typów dla całego modelu.
    ///
    /// <para>
    /// Wartości <see cref="DateTimeOffset"/> są zapisywane jako liczba całkowita (binarna
    /// reprezentacja znacznika czasu UTC). Dostawca SQLite nie potrafi tłumaczyć porównań ani
    /// sortowania na natywnym typie tekstowym <c>DateTimeOffset</c>, co powodowałoby błąd
    /// wykonania przy zapytaniach filtrujących grafik dyżurów po czasie. Zapis w postaci
    /// <c>long</c> pozwala na w pełni serwerowe porównania (BR-02).
    /// </para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
