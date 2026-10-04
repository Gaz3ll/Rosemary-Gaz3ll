using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Application.Mapping;
using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Services;

/// <summary>
/// Serwis pacjenta. Orkiestruje rejestrację, triage, przydział do strefy, zlecenia
/// oraz zamknięcie karty, egzekwując reguły BR-01, BR-04, BR-09, BR-10, BR-19 i BR-20.
/// </summary>
public sealed class PatientService : IPatientService
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthenticationService _authenticationService;

    public PatientService(
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

    public async Task<PatientDetailsDto> RegisterPatientAsync(
        string pesel,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        PatientGender gender,
        string? complaint,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var normalizedPesel = pesel?.Trim() ?? string.Empty;

        var existing = await _unitOfWork.Patients
            .GetByPeselAsync(normalizedPesel, cancellationToken).ConfigureAwait(false);

        if (existing is not null && existing.State != PatientState.Closed)
        {
            throw new SorApplicationException(
                $"Pacjent o numerze PESEL {normalizedPesel} ma już aktywną kartę w SOR (stan: {existing.State}).",
                "SOR-APP-050");
        }

        var patient = Patient.Register(
            _idGenerator.NewId(),
            normalizedPesel,
            firstName,
            lastName,
            dateOfBirth,
            gender,
            complaint,
            now);

        await _unitOfWork.Patients.AddAsync(patient, cancellationToken).ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.PatientRegistered,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(Patient),
            $"Rejestracja pacjenta {patient.FullName} (PESEL {patient.Pesel}); zgłaszane: {complaint ?? "brak"}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patient.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> PerformTriageAsync(
        Guid patientId,
        TriageCategory category,
        string clinicalJustification,
        string vitalSignsSummary,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        patient.AcquireLock(actor.Login, now, LockTimeout);

        var assessment = TriageAssessment.Create(
            _idGenerator.NewId(),
            patient.Id,
            actor.Id,
            category,
            clinicalJustification,
            vitalSignsSummary,
            now);

        patient.AssignTriage(assessment);

        // Ocena jest jawnie dodawana do kontekstu trwałości: EF Core rozpoznałby encję odnalezioną
        // w nawigacji pacjenta (z ustawionym kluczem obcym) jako istniejącą i wykonał aktualizację
        // nieistniejącego wiersza. Jawne dodanie wymusza operację INSERT.
        await _unitOfWork.TriageAssessments.AddAsync(assessment, cancellationToken).ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.TriageAssessed,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(TriageAssessment),
            $"Triage pacjenta {patient.FullName}: {category}; {clinicalJustification}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await AssignToTriageZoneAsync(patient, actor, now, cancellationToken).ConfigureAwait(false);

        // Ocena Triage jest zakończonym krokiem: po jej zapisie karta musi być dostępna dla
        // personelu wykonującego przydział ręczny. Bez zwolnienia blokady (BR-20) następny
        // użytkownik czekałby na jej wygasanie przez LockTimeout.
        patient.ReleaseLock(actor.Login);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Przydział automatyczny (WF-04): pacjent triażowany w strefie klinicznej trafia do tej
    /// strefy, ponieważ personel pracuje w strefie wynikającej z grafiku dyżurów (BR-02), a nie
    /// w strefie wskazanej przez pacjenta. Pracownik modułu triage (TRI) nie przyjmuje pacjenta
    /// sam — przydział odbywa się ręcznie, na podstawie zgłaszanych objawów.
    ///
    /// Brak wolnego miejsca nie cofa oceny Triage: pacjent pozostaje w stanie <c>Triaged</c>
    /// i czeka na przydział ręczny, co odpowiada treści tego stanu w modelu domenowym.
    /// </summary>
    private async Task AssignToTriageZoneAsync(
        Patient patient,
        AuthenticatedUserDto actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (actor.CurrentZoneKind == ZoneKind.Triage)
        {
            await _auditLog.RecordAsync(
                AuditActionType.TriageAssessed,
                actor.Id,
                actor.Login,
                patient.Id,
                nameof(Patient),
                $"Triage w module wstępnym — oczekuje na ręczny przydział do strefy.",
                true,
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var zone = await GetZoneAsync(actor.CurrentZoneId, cancellationToken).ConfigureAwait(false);
        var activeInZone = await _unitOfWork.Patients
            .CountActiveByZoneAsync(zone.Id, cancellationToken).ConfigureAwait(false);

        if (!zone.CanAcceptPatient(activeInZone))
        {
            await _auditLog.RecordAsync(
                AuditActionType.PatientZoneChanged,
                actor.Id,
                actor.Login,
                patient.Id,
                nameof(ZoneTransfer),
                $"Brak wolnych miejsc w strefie '{zone.Name}' — pacjent oczekuje na przydział ręczny.",
                false,
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var transfer = ZoneTransfer.InitialAssignment(
            _idGenerator.NewId(),
            patient.Id,
            zone.Id,
            actor.Id,
            now);

        await RecordZoneAssignmentAsync(
            patient,
            zone,
            actor,
            now,
            transfer,
            $"Przydział do strefy '{zone.Name}' — triage wykonany w tej strefie.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> AssignToZoneAsync(Guid patientId, Guid zoneId, CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        var zone = await GetZoneAsync(zoneId, cancellationToken).ConfigureAwait(false);

        EnsureStaffOfZoneOrCoordinator(actor, zone.Id);

        patient.AcquireLock(actor.Login, now, LockTimeout);

        // BR-04: kontrola zdolności przyjęciowej.
        var activeInZone = await _unitOfWork.Patients
            .CountActiveByZoneAsync(zone.Id, cancellationToken).ConfigureAwait(false);

        try
        {
            zone.EnsureCanAcceptPatient(activeInZone);
        }
        catch (ValidationException)
        {
            throw new ZoneCapacityExceededException(zone.Name, zone.Capacity);
        }

        var transfer = ZoneTransfer.InitialAssignment(
            _idGenerator.NewId(),
            patient.Id,
            zone.Id,
            actor.Id,
            now);

        await RecordZoneAssignmentAsync(
            patient,
            zone,
            actor,
            now,
            transfer,
            $"Przydział pacjenta {patient.FullName} do strefy '{zone.Name}'.",
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wspólny zapis przydziału do strefy: przypisanie pacjenta, historia przeniesień oraz wpis
    /// audytowy. Nie zamyka transakcji — decyzję o commicie podejmuje wywołujący.
    /// </summary>
    private async Task RecordZoneAssignmentAsync(
        Patient patient,
        Zone zone,
        AuthenticatedUserDto actor,
        DateTimeOffset now,
        ZoneTransfer transfer,
        string auditMessage,
        CancellationToken cancellationToken)
    {
        patient.AssignToZone(zone.Id, now);
        patient.RecordTransfer(transfer);

        // Jawne dodanie do kontekstu — patrz komentarz przy ocenie Triage powyżej.
        await _unitOfWork.ZoneTransfers.AddAsync(transfer, cancellationToken).ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.PatientZoneChanged,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(ZoneTransfer),
            auditMessage,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> TransferPatientAsync(
        Guid patientId,
        Guid targetZoneId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        var targetZone = await GetZoneAsync(targetZoneId, cancellationToken).ConfigureAwait(false);

        EnsureStaffOfZoneOrCoordinator(actor, targetZone.Id);
        patient.AcquireLock(actor.Login, now, LockTimeout);

        var fromZoneId = patient.ZoneId ?? Guid.Empty;

        if (fromZoneId == targetZoneId)
        {
            throw new SorApplicationException(
                "Pacjent jest już przypisany do tej strefy — zmiana jest niedozwolona (BR-19).",
                "SOR-APP-051");
        }

        var activeInTarget = await _unitOfWork.Patients
            .CountActiveByZoneAsync(targetZoneId, cancellationToken).ConfigureAwait(false);

        try
        {
            targetZone.EnsureCanAcceptPatient(activeInTarget);
        }
        catch (ValidationException)
        {
            throw new ZoneCapacityExceededException(targetZone.Name, targetZone.Capacity);
        }

        var fromZoneName = fromZoneId == Guid.Empty
            ? "(moduł triage)"
            : (await GetZoneAsync(fromZoneId, cancellationToken).ConfigureAwait(false)).Name;

        var transfer = ZoneTransfer.Create(
            _idGenerator.NewId(),
            patient.Id,
            fromZoneId,
            targetZoneId,
            actor.Id,
            reason,
            now);

        patient.RecordTransfer(transfer);
        patient.AssignToZone(targetZoneId, now);

        await _unitOfWork.ZoneTransfers.AddAsync(transfer, cancellationToken).ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.PatientZoneChanged,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(ZoneTransfer),
            $"Przeniesienie pacjenta {patient.FullName}: {fromZoneName} → {targetZone.Name}; powód: {reason}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> AddOrderAsync(
        Guid patientId,
        MedicalOrderType type,
        string description,
        bool isUrgent,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        // BR-13: zlecenie lekarskie może wystawić wyłącznie lekarz lub koordynator.
        // Ratownik medyczny i pielęgniarka realizują zlecenia, ale ich nie wystawiają.
        if (!actor.CanIssueOrders)
        {
            throw new AuthorizationException(
                "Zlecenie lekarskie może wystawić wyłącznie lekarz lub koordynator (BR-13).");
        }

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        patient.AcquireLock(actor.Login, now, LockTimeout);

        var order = MedicalOrder.Create(
            _idGenerator.NewId(),
            patient.Id,
            actor.Id,
            actor.Role,
            type,
            description,
            now,
            isUrgent);

        patient.AddOrder(order);

        // Jawne dodanie do kontekstu — kolejka EF Core nie zgłaszałaby zlecenia
        // odnalezionego w kolekcji pacjenta jako Added, a wykonałaby UPDATE
        // nieistniejącego wiersza (analogicznie do ocen Triage i przeniesień).
        await _unitOfWork.MedicalOrders
            .AddAsync(order, cancellationToken)
            .ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.OrderCreated,
            actor.Id,
            actor.Login,
            order.Id,
            nameof(MedicalOrder),
            $"Zlecenie {type} dla pacjenta {patient.FullName}: {description}{(isUrgent ? " (pilne)" : string.Empty)}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> ChangeOrderStateAsync(
        Guid orderId,
        MedicalOrderState newState,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var order = await _unitOfWork.MedicalOrders
            .GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MedicalOrder), orderId);

        if (newState == MedicalOrderState.Completed)
        {
            order.EnsureCanBeCompletedBy(actor.Role);
        }

        order.ChangeState(newState, now, reason);

        await _auditLog.RecordAsync(
            AuditActionType.OrderStatusChanged,
            actor.Id,
            actor.Login,
            order.Id,
            nameof(MedicalOrder),
            $"Zmiana statusu zlecenia na '{newState}'{((reason is null) ? string.Empty : $"; powód: {reason}")}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(order.PatientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> SetDiagnosisAsync(Guid patientId, string icd10Code, CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();

        // BR-13: rozpoznanie może wystawić wyłącznie lekarz lub koordynator.
        if (!actor.CanEnterDiagnosis)
        {
            throw new AuthorizationException(
                "Rozpoznanie ICD-10 może wprowadzić wyłącznie lekarz lub koordynator (BR-13).");
        }

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        var diagnosis = Icd10Code.Create(icd10Code);

        patient.AcquireLock(actor.Login, _clock.UtcNow, LockTimeout);
        patient.SetDiagnosis(diagnosis);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> RecordMedicationAdministrationAsync(
        Guid patientId,
        Guid medicationId,
        string dose,
        MedicationRoute route,
        Guid? medicalOrderId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        patient.AcquireLock(actor.Login, now, LockTimeout);

        var medication = await _unitOfWork.Medications
            .GetByIdAsync(medicationId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Medication), medicationId);

        if (!medication.IsAvailable)
        {
            throw new SorApplicationException(
                $"Preparat '{medication.DisplayName}' został wycofany z formularza oddziału.",
                "SOR-APP-053");
        }

        // Dawka podana musi pochodzić z katalogu — inaczej pacjentowi podano by lek spoza SOR.
        if (!MatchesCatalogDose(medication, dose))
        {
            throw new SorApplicationException(
                $"Dawka „{dose}” nie odpowiada pozycji katalogowej leku {medication.Code} " +
                $"(typowo: {medication.TypicalDose}).",
                "SOR-APP-054");
        }

        var administration = MedicationAdministration.Record(
            _idGenerator.NewId(),
            patient.Id,
            medication.Id,
            actor.Id,
            dose,
            route,
            now,
            medicalOrderId,
            notes);

        patient.RecordAdministration(administration);

        // Jawne dodanie do kontekstu wymusza INSERT (analogicznie do ocen Triage i przeniesień).
        await _unitOfWork.MedicationAdministrations
            .AddAsync(administration, cancellationToken)
            .ConfigureAwait(false);

        if (medicalOrderId is not null)
        {
            var order = await _unitOfWork.MedicalOrders
                .GetByIdAsync(medicalOrderId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (order is not null && order.PatientId == patient.Id && order.State == MedicalOrderState.Open)
            {
                order.ChangeState(MedicalOrderState.InProgress, now, reason: null);
            }
        }

        await _auditLog.RecordAsync(
            AuditActionType.MedicationAdministered,
            actor.Id,
            actor.Login,
            administration.Id,
            nameof(MedicationAdministration),
            $"Podano {medication.DisplayName} w dawce {dose}, droga: {route}; pacjent {patient.FullName}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Luźne dopasowanie dawki do katalogu: porównujemy znormalizowany zapis, aby dopuścić
    /// równoważne zapisy (np. „1 g" wobec „1000 mg"), ale odrzucamy dawki spoza katalogu.
    /// </summary>
    private static bool MatchesCatalogDose(Medication medication, string dose)
    {
        if (string.IsNullOrWhiteSpace(dose))
        {
            return false;
        }

        var provided = NormalizeDose(dose);
        var typical = NormalizeDose(medication.TypicalDose);
        var max = NormalizeDose(medication.MaxDailyDose);

        return provided.Contains(typical, StringComparison.Ordinal) || provided.Contains(max, StringComparison.Ordinal);
    }

    private static string NormalizeDose(string value) =>
        new string(value
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or ',' or '-')
            .Select(c => char.IsLetter(c) ? char.ToLowerInvariant(c) : c == ',' ? '.' : c)
            .ToArray());


    /// <summary>
    /// Wypis pacjenta z SOR (BR-11). Obsługuje trzy scenariusze: zakończenie leczenia,
    /// wypis na własne żądanie oraz przekazanie na inny oddział. Decyzję podejmuje lekarz
    /// lub koordynator (BR-13), a każda próba — udana lub odrzucona — trafia do dziennika audytu.
    /// </summary>
    public async Task<PatientDetailsDto> DischargePatientAsync(
        Guid patientId,
        DischargeType type,
        Guid? departmentId = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        if (!actor.CanDischargePatient)
        {
            throw new AuthorizationException(
                "Wypis pacjenta z SOR może zarejestrować wyłącznie lekarz lub koordynator (BR-13).");
        }

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);

        if (patient.ZoneId is not null)
        {
            EnsureStaffOfZoneOrCoordinator(actor, patient.ZoneId.Value);
        }

        Department? department = null;

        if (type == DischargeType.TransferToDepartment)
        {
            if (departmentId is null)
            {
                throw new ValidationException(
                    "Przekazanie na inny oddział wymaga wskazania oddziału przyjmującego (BR-11).",
                    nameof(departmentId));
            }

            department = await _unitOfWork.Departments
                .GetByIdAsync(departmentId.Value, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EntityNotFoundException(nameof(Department), departmentId.Value);
        }

        var hasReason = !string.IsNullOrWhiteSpace(reason);
        var blockers = patient.GetDischargeBlockers(type, department is not null, hasReason);

        if (blockers.Count > 0)
        {
            await _auditLog.RecordAsync(
                AuditActionType.PatientDischargeBlocked,
                actor.Id,
                actor.Login,
                patient.Id,
                nameof(Patient),
                $"Odrzucono wypis pacjenta {patient.FullName} ({DescribeDischargeType(type)}): {string.Join("; ", blockers)}.",
                false,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            throw new PatientDischargeBlockedException(blockers.ToArray());
        }

        var discharge = patient.Discharge(
            _idGenerator.NewId(),
            type,
            now,
            actor.Id,
            actor.Login,
            department,
            reason);

        // Jawne dodanie do kontekstu — wpis wypisu ma nadany klucz główny w domenie,
        // więc bez tego EF Core potraktowałby go jako istniejący i wykonał UPDATE
        // nieistniejącego wiersza zamiast INSERT (analogicznie do zleceń i ocen Triage).
        await _unitOfWork.PatientDischarges
            .AddAsync(discharge, cancellationToken)
            .ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.PatientDischarged,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(Patient),
            BuildDischargeAuditDetails(patient, discharge),
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDischargeAuditDetails(Patient patient, PatientDischarge discharge) =>
        discharge.Type switch
        {
            DischargeType.AtPatientRequest =>
                $"Wypis na własne żądanie pacjenta {patient.FullName}; rozpoznanie: {patient.Diagnosis?.Value ?? "brak"}; powód: {discharge.Reason}.",
            DischargeType.TransferToDepartment =>
                $"Przekazanie pacjenta {patient.FullName} na oddział: {discharge.DepartmentName}; rozpoznanie: {patient.Diagnosis?.Value ?? "brak"}.",
            _ =>
                $"Zakończono leczenie pacjenta {patient.FullName}; rozpoznanie: {patient.Diagnosis?.Value ?? "brak"}."
        };

    private static string DescribeDischargeType(DischargeType type) => type switch
    {
        DischargeType.AtPatientRequest => "wypis na własne żądanie",
        DischargeType.TransferToDepartment => "przekazanie na inny oddział",
        _ => "zakończenie leczenia"
    };

    public async Task<PatientDetailsDto> LockCardAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);

        try
        {
            patient.AcquireLock(actor.Login, _clock.UtcNow, LockTimeout);
        }
        catch (ConcurrentPatientModificationException ex)
        {
            await RecordConcurrencyConflictAsync(actor, patient.Id, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PatientDetailsDto> UnlockCardAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);

        try
        {
            patient.ReleaseLock(actor.Login);
        }
        catch (ConcurrentPatientModificationException ex)
        {
            await RecordConcurrencyConflictAsync(actor, patient.Id, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PatientCardDto>> GetZonePatientsAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var patients = await _unitOfWork.Patients
            .GetByZoneAsync(zoneId, cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;

        return patients
            .OrderByDescending(p => p.CurrentTriage?.Category ?? TriageCategory.Blue)
            .ThenBy(p => p.ZoneAssignedAtUtc)
            .Select(p => p.ToCardDto(now))
            .ToList();
    }

    public async Task<IReadOnlyList<PatientCardDto>> GetAwaitingTriageAsync(CancellationToken cancellationToken = default)
    {
        var patients = await _unitOfWork.Patients
            .GetAwaitingTriageAsync(cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;

        return patients
            .OrderBy(p => p.RegisteredAtUtc)
            .Select(p => p.ToCardDto(now))
            .ToList();
    }

    public async Task<PatientDetailsDto> GetPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);
        var zoneName = patient.ZoneId is null
            ? null
            : (await _unitOfWork.Zones.GetByIdAsync(patient.ZoneId.Value, cancellationToken).ConfigureAwait(false))?.Name;

        var medicationNames = await LoadMedicationNamesAsync(patient, cancellationToken).ConfigureAwait(false);

        return patient.ToDetailsDto(zoneName, _clock.UtcNow, medicationNames);
    }

    /// <summary>Nazwy preparatów występujących w rejestrze podanych leków pacjenta.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> LoadMedicationNamesAsync(
        Patient patient,
        CancellationToken cancellationToken)
    {
        var medicationIds = patient.Administrations
            .Select(a => a.MedicationId)
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

    // ---------- Pomocnicze ----------

    private async Task<Patient> GetPatientEntityAsync(Guid patientId, CancellationToken cancellationToken) =>
        await _unitOfWork.Patients
            .GetByIdAsync(patientId, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException(nameof(Patient), patientId);

    private async Task<Zone> GetZoneAsync(Guid zoneId, CancellationToken cancellationToken) =>
        await _unitOfWork.Zones
            .GetByIdAsync(zoneId, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException(nameof(Zone), zoneId);

    /// <summary>BR-12: pracownik operuje na pacjentach własnej strefy; koordynator — wszystkich.</summary>
    private static void EnsureStaffOfZoneOrCoordinator(AuthenticatedUserDto actor, Guid zoneId)
    {
        if (actor.Role != UserRole.Coordinator && actor.CurrentZoneId != zoneId)
        {
            throw new AuthorizationException(
                $"Pacjent należy do innej strefy niż Twoja ({actor.CurrentZoneName}). " +
                "Zmianę może wykonać koordynator (BR-12).");
        }
    }

    private async Task RecordConcurrencyConflictAsync(
        AuthenticatedUserDto actor,
        Guid patientId,
        ConcurrentPatientModificationException ex,
        CancellationToken cancellationToken)
    {
        await _auditLog.RecordAsync(
            AuditActionType.ConcurrentModificationBlocked,
            actor.Id,
            actor.Login,
            patientId,
            nameof(Patient),
            $"Odrzucono równoległą modyfikację karty pacjenta — karta zablokowana przez '{ex.HeldBy}' (BR-20).",
            false,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private AuthenticatedUserDto RequireAuthenticatedUser() =>
        _authenticationService.CurrentUser
        ?? throw new AuthenticationException("Operacja wymaga aktywnej sesji użytkownika.");
}