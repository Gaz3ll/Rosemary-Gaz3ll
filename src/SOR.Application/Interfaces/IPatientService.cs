using SOR.Application.DTOs;
using SOR.Domain.Enums;

namespace SOR.Application.Interfaces;

/// <summary>Serwis pacjenta — rejestracja, triage, przypisanie do strefy, zlecenia, zamknięcie karty.</summary>
public interface IPatientService
{
    /// <summary>Rejestracja pacjenta w module wstępnym.</summary>
    Task<PatientDetailsDto> RegisterPatientAsync(
        string pesel,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        PatientGender gender,
        string? complaint,
        CancellationToken cancellationToken = default);

    /// <summary>Wykonanie oceny Triage z automatyczną rekomendacją strefy docelowej.</summary>
    Task<PatientDetailsDto> PerformTriageAsync(
        Guid patientId,
        TriageCategory category,
        string clinicalJustification,
        string vitalSignsSummary,
        CancellationToken cancellationToken = default);

    /// <summary>Przypisanie pacjenta do strefy z walidacją zdolności przyjęciowej (BR-04).</summary>
    Task<PatientDetailsDto> AssignToZoneAsync(Guid patientId, Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Przeniesienie pacjenta między strefami z uzasadnieniem (BR-19).</summary>
    Task<PatientDetailsDto> TransferPatientAsync(
        Guid patientId,
        Guid targetZoneId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Dodanie zlecenia lekarskiego.</summary>
    Task<PatientDetailsDto> AddOrderAsync(
        Guid patientId,
        MedicalOrderType type,
        string description,
        bool isUrgent,
        CancellationToken cancellationToken = default);

    /// <summary>Zmiana statusu zlecenia.</summary>
    Task<PatientDetailsDto> ChangeOrderStateAsync(
        Guid orderId,
        MedicalOrderState newState,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Odnotowanie faktycznego podania leku z katalogu SOR. Dawka jest weryfikowana
    /// względem pozycji katalogowej, a powiązane zlecenie przechodzi w stan „w realizacji".
    /// </summary>
    Task<PatientDetailsDto> RecordMedicationAdministrationAsync(
        Guid patientId,
        Guid medicationId,
        string dose,
        MedicationRoute route,
        Guid? medicalOrderId = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ustawienie rozpoznania ICD-10.</summary>
    Task<PatientDetailsDto> SetDiagnosisAsync(Guid patientId, string icd10Code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wypis pacjenta z SOR (BR-11): zakończenie leczenia, wypis na własne żądanie
    /// albo przekazanie na inny oddział.
    /// </summary>
    /// <param name="patientId">Identyfikator pacjenta.</param>
    /// <param name="type">Sposób zakończenia pobytu.</param>
    /// <param name="departmentId">Oddział przyjmujący — wymagany przy przekazaniu.</param>
    /// <param name="reason">Uzasadnienie — wymagane przy wypisie na własne żądanie i przy przekazaniu.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    Task<PatientDetailsDto> DischargePatientAsync(
        Guid patientId,
        DischargeType type,
        Guid? departmentId = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>Przejęcie blokady karty (BR-20).</summary>
    Task<PatientDetailsDto> LockCardAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>Zwolnienie blokady karty.</summary>
    Task<PatientDetailsDto> UnlockCardAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>Pobiera pacjentów strefy (do widoku pulpitu).</summary>
    Task<IReadOnlyList<PatientCardDto>> GetZonePatientsAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Pobiera pacjentów oczekujących na triage.</summary>
    Task<IReadOnlyList<PatientCardDto>> GetAwaitingTriageAsync(CancellationToken cancellationToken = default);

    /// <summary>Pobiera szczegóły karty pacjenta.</summary>
    Task<PatientDetailsDto> GetPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>Serwis dziennika audytu (wymóg formalny ścieżki audytowej).</summary>
public interface IAuditLogService
{
    /// <summary>Zapisuje wpis audytowy w bieżącej transakcji.</summary>
    Task<Guid> RecordAsync(
        AuditActionType actionType,
        Guid? userId,
        string actorLogin,
        Guid? entityId,
        string? entityType,
        string details,
        bool success = true,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera ostatnie wpisy audytowe (widok koordynatora).</summary>
    Task<IReadOnlyList<AuditLogEntryDto>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Pobiera pełną historię rotacji danego użytkownika do wglądu.</summary>
    Task<IReadOnlyList<AuditLogEntryDto>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Wpis dziennika audytu prezentowany w interfejsie.</summary>
public sealed record AuditLogEntryDto(
    Guid Id,
    AuditActionType ActionType,
    string ActorLogin,
    string? ActorDisplayName,
    string Details,
    DateTimeOffset OccurredAtUtc,
    bool Success);

/// <summary>
/// Serwis listy pobytów — zasilanie widoku „Lista pobytów” oraz zakładek „Raport z dyżuru”
/// i „Badania obrazowe”. Operacje tylko do odczytu.
/// </summary>
public interface IPatientStayService
{
    /// <summary>Pobiera pobyty z zakresu dat, przefiltrowane według statusu.</summary>
    /// <param name="query">Zakres dat i filtr statusu z panelu filtrów.</param>
    /// <param name="nowUtc">Bieżący czas — podstawa wyliczenia czasu pobytu w strefie.</param>
    /// <param name="cancellationToken">Token anulowania.</param>
    Task<IReadOnlyList<PatientStayDto>> GetStaysAsync(
        StayQuery query,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Zlecenia badań obrazowych i konsultacji pacjentów z listy pobytów.</summary>
    Task<IReadOnlyList<ImagingStudyDto>> GetImagingStudiesAsync(
        StayQuery query,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
