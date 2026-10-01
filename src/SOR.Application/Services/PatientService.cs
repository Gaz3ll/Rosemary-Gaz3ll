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

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
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

        patient.AssignToZone(zone.Id, now);

        var transfer = ZoneTransfer.InitialAssignment(
            _idGenerator.NewId(),
            patient.Id,
            zone.Id,
            actor.Id,
            now);

        patient.RecordTransfer(transfer);

        // Jawne dodanie do kontekstu — patrz komentarz przy ocenie Triage powyżej.
        await _unitOfWork.ZoneTransfers.AddAsync(transfer, cancellationToken).ConfigureAwait(false);

        await _auditLog.RecordAsync(
            AuditActionType.PatientZoneChanged,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(ZoneTransfer),
            $"Przydział pacjenta {patient.FullName} do strefy '{zone.Name}'.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
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
        if (actor.Role == UserRole.Nurse)
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

    public async Task<PatientDetailsDto> CloseCardAsync(Guid patientId, bool transportCompleted, CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var now = _clock.UtcNow;

        var patient = await GetPatientEntityAsync(patientId, cancellationToken).ConfigureAwait(false);

        if (actor.Role == UserRole.Nurse && !transportCompleted)
        {
            throw new AuthorizationException(
                "Potwierdzenie transportu pacjenta wymaga uprawnień lekarza lub koordynatora (BR-13).");
        }

        var blockers = patient.CheckClosureBlockers(transportCompleted);

        if (blockers.Count > 0)
        {
            await _auditLog.RecordAsync(
                AuditActionType.PatientCardClosureAttempt,
                actor.Id,
                actor.Login,
                patient.Id,
                nameof(Patient),
                $"Odrzucono zamknięcie karty pacjenta {patient.FullName}: {string.Join("; ", blockers)}.",
                false,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            throw new PatientCardClosureBlockedException(blockers.ToArray());
        }

        patient.Close(now, transportCompleted);

        await _auditLog.RecordAsync(
            AuditActionType.PatientCardClosureAttempt,
            actor.Id,
            actor.Login,
            patient.Id,
            nameof(Patient),
            $"Zamknięto kartę pacjenta {patient.FullName}; rozpoznanie: {patient.Diagnosis?.Value}.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return await GetPatientAsync(patientId, cancellationToken).ConfigureAwait(false);
    }

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

        return patient.ToDetailsDto(zoneName, _clock.UtcNow);
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