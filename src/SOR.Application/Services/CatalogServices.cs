using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Application.Mapping;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Services;

/// <summary>Serwis katalogu leków — odczyt formularza SOR.</summary>
public sealed class MedicationCatalogService : IMedicationCatalogService
{
    private readonly IUnitOfWork _unitOfWork;

    public MedicationCatalogService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<IReadOnlyList<MedicationDto>> SearchAsync(
        string? term,
        MedicationCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        var medications = await _unitOfWork.Medications
            .SearchAsync(term, category, cancellationToken)
            .ConfigureAwait(false);

        return medications.Select(m => m.ToDto()).ToList();
    }

    public async Task<MedicationDto> GetAsync(Guid medicationId, CancellationToken cancellationToken = default)
    {
        var medication = await _unitOfWork.Medications
            .GetByIdAsync(medicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Medication), medicationId);

        return medication.ToDto();
    }

    public async Task<IReadOnlyList<MedicationDto>> GetHighAlertAsync(CancellationToken cancellationToken = default)
    {
        var medications = await _unitOfWork.Medications
            .GetHighAlertAsync(cancellationToken)
            .ConfigureAwait(false);

        return medications.Select(m => m.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<MedicationCategoryOptionDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var medications = await _unitOfWork.Medications
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return medications
            .Where(m => m.IsAvailable)
            .GroupBy(m => m.Category)
            .OrderBy(g => (int)g.Key)
            .Select(g => new MedicationCategoryOptionDto(g.Key, g.Count()))
            .ToList();
    }
}

/// <summary>Serwis pakietów medycznych — definicje pakietów oraz ich zastosowanie na karcie pacjenta.</summary>
public sealed class MedicalBundleService : IMedicalBundleService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthenticationService _authenticationService;

    public MedicalBundleService(
        IUnitOfWork unitOfWork,
        IClock clock,
        IIdGenerator idGenerator,
        IAuditLogService auditLog,
        IAuthenticationService authenticationService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    public async Task<IReadOnlyList<MedicalBundleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var bundles = await _unitOfWork.MedicalBundles
            .GetActiveWithItemsAsync(cancellationToken)
            .ConfigureAwait(false);

        return await MapAsync(bundles, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MedicalBundleDto>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var bundles = await _unitOfWork.MedicalBundles
            .SearchAsync(term, cancellationToken)
            .ConfigureAwait(false);

        return await MapAsync(bundles, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MedicalBundleDto> GetAsync(Guid bundleId, CancellationToken cancellationToken = default)
    {
        var bundle = await _unitOfWork.MedicalBundles
            .GetWithItemsAsync(bundleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MedicalBundle), bundleId);

        var names = await ResolveMedicationNamesAsync(bundle, cancellationToken).ConfigureAwait(false);
        return bundle.ToDto(names);
    }

    public async Task<PatientDetailsDto> ApplyAsync(
        Guid patientId,
        Guid bundleId,
        IReadOnlyCollection<Guid>? itemIds = null,
        CancellationToken cancellationToken = default)
    {
        // Zlecenie lekarskie może wystawić wyłącznie lekarz lub koordynator (BR-13).
        var actor = _authenticationService.CurrentUser
            ?? throw new AuthenticationException("Operacja wymaga aktywnej sesji użytkownika.");

        if (!actor.CanIssueOrders)
        {
            throw new AuthorizationException(
                "Zastosowanie pakietu medycznego wymaga uprawnień lekarza lub koordynatora (BR-13).");
        }

        var now = _clock.UtcNow;

        var patient = await _unitOfWork.Patients
            .GetByIdAsync(patientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Patient), patientId);

        patient.AcquireLock(actor.Login, now, LockTimeout);

        var bundle = await _unitOfWork.MedicalBundles
            .GetWithItemsAsync(bundleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MedicalBundle), bundleId);

        var selected = bundle.Items
            .OrderBy(i => i.Sequence)
            .Where(i => itemIds is null || itemIds.Contains(i.Id))
            .ToList();

        if (selected.Count == 0)
        {
            throw new SorApplicationException("Nie wybrano żadnego kroku pakietu do zastosowania.", "SOR-APP-051");
        }

        var createdOrders = 0;
        var createdAdministrations = 0;

        foreach (var item in selected)
        {
            MedicalOrder? order = null;

            if (item.RequiresOrder)
            {
                order = MedicalOrder.Create(
                    _idGenerator.NewId(),
                    patient.Id,
                    actor.Id,
                    actor.Role,
                    item.OrderType,
                    item.Description,
                    now,
                    item.IsUrgent);

                patient.AddOrder(order);

                // Jawne dodanie do kontekstu — kolejka EF Core nie zgłaszałaby nowego
                // zlecenia dodanego przez kolekcję pacjenta jako Added, a wykonałaby
                // UPDATE nieistniejącego wiersza.
                await _unitOfWork.MedicalOrders
                    .AddAsync(order, cancellationToken)
                    .ConfigureAwait(false);

                createdOrders++;
            }

            // Pozycja farmakologiczna od razu trafia do rejestru podanych leków — pakiet
            // opisuje postępowanie wykonywane w trakcie przyjęcia, a nie kolejne zlecenia.
            if (item.MedicationId is not null && item.Route is not null && !string.IsNullOrWhiteSpace(item.Dose))
            {
                var administration = MedicationAdministration.Record(
                    _idGenerator.NewId(),
                    patient.Id,
                    item.MedicationId.Value,
                    actor.Id,
                    item.Dose!,
                    item.Route.Value,
                    now,
                    order?.Id,
                    $"Pakiet: {bundle.Name} (pozycja {item.Sequence})");

                patient.RecordAdministration(administration);

                // Jawne dodanie do kontekstu — kolejka EF Core nie zgłaszałaby podania
                // dodanego przez kolekcję pacjenta jako Added, a wykonałaby UPDATE
                // nieistniejącego wiersza.
                await _unitOfWork.MedicationAdministrations
                    .AddAsync(administration, cancellationToken)
                    .ConfigureAwait(false);

                createdAdministrations++;
            }
        }

        await _auditLog.RecordAsync(
            AuditActionType.MedicalBundleApplied,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(MedicalBundle),
            $"Zastosowano pakiet '{bundle.Name}' ({bundle.Code}) dla pacjenta {patient.FullName}: " +
            $"{selected.Count} kroków, zleceń: {createdOrders}, podanych leków: {createdAdministrations}.",
            true,
            cancellationToken).ConfigureAwait(false);

        if (createdAdministrations > 0)
        {
            await _auditLog.RecordAsync(
                AuditActionType.MedicationAdministered,
                actor.Id,
                actor.Login,
                patient.Id,
                nameof(MedicationAdministration),
                $"Podano {createdAdministrations} lek(ów) z pakietu '{bundle.Name}' pacjentowi {patient.FullName}.",
                true,
                cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientDetailsAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MedicalBundleDto>> MapAsync(
        IReadOnlyList<MedicalBundle> bundles,
        CancellationToken cancellationToken)
    {
        var result = new List<MedicalBundleDto>(bundles.Count);

        foreach (var bundle in bundles)
        {
            var names = await ResolveMedicationNamesAsync(bundle, cancellationToken).ConfigureAwait(false);
            result.Add(bundle.ToDto(names));
        }

        return result;
    }

    /// <summary>Nazwy preparatów występujących w krokach pakietu — pobierane zbiorczo, aby uniknąć N+1.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveMedicationNamesAsync(
        MedicalBundle bundle,
        CancellationToken cancellationToken)
    {
        var medicationIds = bundle.Items
            .Where(i => i.MedicationId is not null)
            .Select(i => i.MedicationId!.Value)
            .Distinct()
            .ToList();

        if (medicationIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var medications = await _unitOfWork.Medications
            .GetByIdsAsync(medicationIds, cancellationToken)
            .ConfigureAwait(false);

        return medications.ToDictionary(m => m.Id, m => m.DisplayName);
    }

    private async Task<PatientDetailsDto> GetPatientDetailsAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var patient = await _unitOfWork.Patients
            .GetByIdAsync(patientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Patient), patientId);

        var zoneName = patient.ZoneId is null
            ? null
            : (await _unitOfWork.Zones.GetByIdAsync(patient.ZoneId.Value, cancellationToken).ConfigureAwait(false))?.Name;

        return patient.ToDetailsDto(zoneName, _clock.UtcNow);
    }
}

/// <summary>Serwis katalogu rozpoznań ICD-10.</summary>
public sealed class Icd10CatalogService : IIcd10CatalogService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthenticationService _authenticationService;

    public Icd10CatalogService(
        IUnitOfWork unitOfWork,
        IClock clock,
        IAuditLogService auditLog,
        IAuthenticationService authenticationService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    public async Task<IReadOnlyList<Icd10CatalogEntryDto>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var entries = await _unitOfWork.Icd10Catalog
            .SearchAsync(term, cancellationToken)
            .ConfigureAwait(false);

        return entries.Select(e => e.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<Icd10CatalogEntryDto>> GetEmergencyRelevantAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await _unitOfWork.Icd10Catalog
            .SearchAsync(term: null, cancellationToken)
            .ConfigureAwait(false);

        return entries.Where(e => e.IsEmergencyRelevant).Select(e => e.ToDto()).ToList();
    }

    public async Task<PatientDetailsDto> AssignAsync(
        Guid patientId,
        string? icd10Code,
        CancellationToken cancellationToken = default)
    {
        var actor = _authenticationService.CurrentUser
            ?? throw new AuthenticationException("Operacja wymaga aktywnej sesji użytkownika.");

        // BR-13: rozpoznanie może wystawić wyłącznie lekarz lub koordynator.
        if (!actor.CanEnterDiagnosis)
        {
            throw new AuthorizationException(
                "Rozpoznanie ICD-10 może wprowadzić wyłącznie lekarz lub koordynator (BR-13).");
        }

        var patient = await _unitOfWork.Patients
            .GetByIdAsync(patientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Patient), patientId);

        patient.AcquireLock(actor.Login, _clock.UtcNow, LockTimeout);

        string description;

        if (string.IsNullOrWhiteSpace(icd10Code))
        {
            patient.ClearDiagnosis();
            description = "Cofnięto rozpoznanie ICD-10";
        }
        else
        {
            var entry = await _unitOfWork.Icd10Catalog
                .GetByCodeAsync(icd10Code, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new SorApplicationException(
                    $"Kod '{icd10Code.Trim().ToUpperInvariant()}' nie występuje w katalogu rozpoznań.",
                    "SOR-APP-052");

            patient.SetDiagnosis(Icd10Code.Create(entry.Code));
            description = $"Ustawiono rozpoznanie {entry.ToDto().DisplayName}";
        }

        await _auditLog.RecordAsync(
            AuditActionType.DiagnosisChanged,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(Patient),
            $"{description} pacjenta {patient.FullName}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        var zoneName = patient.ZoneId is null
            ? null
            : (await _unitOfWork.Zones.GetByIdAsync(patient.ZoneId.Value, cancellationToken).ConfigureAwait(false))?.Name;

        return patient.ToDetailsDto(zoneName, _clock.UtcNow);
    }
}

/// <summary>Serwis katalogu oddziałów szpitalnych — wybór oddziału przy przekazaniu pacjenta (BR-11).</summary>
public sealed class DepartmentCatalogService : IDepartmentCatalogService
{
    private readonly IUnitOfWork _unitOfWork;

    public DepartmentCatalogService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<IReadOnlyList<DepartmentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var departments = await _unitOfWork.Departments
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        return departments.Select(department => department.ToDto()).ToList();
    }
}
