using Microsoft.EntityFrameworkCore;
using SOR.Application.Interfaces;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence.Repositories;

/// <summary>Repozytorium katalogu leków — wyszukiwanie po wolnym tekście i filtrze kategorii.</summary>
public sealed class MedicationRepository : EntityFrameworkRepository<Medication>, IMedicationRepository
{
    public MedicationRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Medication>> SearchAsync(
        string? term,
        MedicationCategory? category,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Medications.AsNoTracking();

        if (category is not null)
        {
            var wanted = category.Value;
            query = query.Where(m => m.Category == wanted);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            // Wyrażenie jest porównywane w pamięci, aby zachować zgodność z bazą SQLite,
            // której domyślne ustawienie porównywania tekstu jest wrażliwe na wielkość liter.
            var normalized = term.Trim().ToUpperInvariant();
            var filtered = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            return filtered
                .Where(m =>
                    Contains(m.Name, normalized) ||
                    Contains(m.Code, normalized) ||
                    Contains(m.Form, normalized) ||
                    Contains(m.Strength, normalized))
                .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return await query
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Medication>> GetHighAlertAsync(CancellationToken cancellationToken = default) =>
        await Context.Medications
            .AsNoTracking()
            .Where(m => m.Safety == MedicationSafety.HighAlert || m.Safety == MedicationSafety.BloodProduct)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static bool Contains(string? value, string normalizedTerm) =>
        value is not null && value.ToUpperInvariant().Contains(normalizedTerm, StringComparison.Ordinal);
}

/// <summary>Repozytorium pakietów medycznych wraz z ich krokami.</summary>
public sealed class MedicalBundleRepository : EntityFrameworkRepository<MedicalBundle>, IMedicalBundleRepository
{
    public MedicalBundleRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<MedicalBundle?> GetWithItemsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Context.MedicalBundles
            .AsNoTracking()
            .Include(b => b.Items.OrderBy(i => i.Sequence))
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<MedicalBundle>> GetActiveWithItemsAsync(CancellationToken cancellationToken = default) =>
        await Context.MedicalBundles
            .AsNoTracking()
            .Include(b => b.Items.OrderBy(i => i.Sequence))
            .OrderBy(b => b.Chapter)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<MedicalBundle>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var all = await GetActiveWithItemsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(term))
        {
            return all;
        }

        var normalized = term.Trim().ToUpperInvariant();
        return all
            .Where(b =>
                b.Name.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal) ||
                b.Code.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal) ||
                b.Indication.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal))
            .ToList();
    }
}

/// <summary>Repozytorium rejestru podanych leków — jawnie dodanie wymusza INSERT.</summary>
public sealed class MedicationAdministrationRepository
    : EntityFrameworkRepository<MedicationAdministration>, IMedicationAdministrationRepository
{
    public MedicationAdministrationRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MedicationAdministration>> GetByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default) =>
        await Context.MedicationAdministrations
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AdministeredAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Repozytorium katalogu rozpoznań ICD-10.</summary>
public sealed class Icd10CatalogRepository : EntityFrameworkRepository<Icd10CatalogEntry>, IIcd10CatalogRepository
{
    public Icd10CatalogRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Icd10CatalogEntry>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var query = Context.Icd10CatalogEntries.AsNoTracking();

        if (string.IsNullOrWhiteSpace(term))
        {
            return await query
                .OrderBy(e => e.Code)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var normalized = term.Trim().ToUpperInvariant();
        var filtered = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        return filtered
            .Where(e =>
                e.Code.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal) ||
                e.Description.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal))
            .OrderBy(e => e.Code)
            .ToList();
    }

    public async Task<Icd10CatalogEntry?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await Context.Icd10CatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Code == code.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Repozytorium katalogu oddziałów szpitalnych — listowanie i odczyt po kodzie.</summary>
public sealed class DepartmentRepository : EntityFrameworkRepository<Department>, IDepartmentRepository
{
    public DepartmentRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Department?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await Context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == code.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Repozytorium wpisów wypisów — historia zakończenia pobytu pacjenta w SOR.</summary>
public sealed class PatientDischargeRepository
    : EntityFrameworkRepository<PatientDischarge>, IPatientDischargeRepository
{
    public PatientDischargeRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<PatientDischarge>> GetByPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default) =>
        await Context.PatientDischarges
            .AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.DischargedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
