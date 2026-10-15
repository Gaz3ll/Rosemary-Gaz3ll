using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>Widok karty pacjenta na pulpicie strefy.</summary>
public sealed record PatientCardDto(
    Guid Id,
    string Pesel,
    string FullName,
    int Age,
    TriageCategory Triage,
    PatientState State,
    string? Complaint,
    TimeSpan TimeInZone,
    TimeSpan MaxWaitTime,
    bool IsWaitingTimeExceeded,
    bool IsLocked,
    string? LockedBy,
    int OpenOrderCount);

/// <summary>
/// Pełny widok karty pacjenta (okno szczegółów oraz okno „Historia pobytu”): dane przyjęcia,
/// historia ocen Triage, przeniesienia między strefami, zlecenia, podane leki i wypisy.
/// </summary>
public sealed record PatientDetailsDto(
    Guid Id,
    string Pesel,
    string FullName,
    DateOnly DateOfBirth,
    PatientGender Gender,
    string? Complaint,
    TriageCategory Triage,
    string? Diagnosis,
    PatientState State,
    Guid? ZoneId,
    string? ZoneName,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? ZoneAssignedAtUtc,
    IReadOnlyList<TriageAssessmentDto> TriageHistory,
    IReadOnlyList<MedicalOrderDto> Orders,
    IReadOnlyList<ZoneTransferDto> Transfers,
    IReadOnlyList<MedicationAdministrationDto> Administrations,
    IReadOnlyList<PatientDischargeDto> Discharges,
    IReadOnlyList<string> DischargeBlockers);

/// <summary>
/// Wpis historii ocen Triage. Ocena raz nadana nie jest nadpisywana (kolejna ocena
/// dopisuje się do historii), więc pełna lista pokazuje przebieg segregacji pacjenta.
/// </summary>
public sealed record TriageAssessmentDto(
    Guid Id,
    TriageCategory Category,
    string ClinicalJustification,
    string VitalSignsSummary,
    DateTimeOffset AssessedAtUtc)
{
    /// <summary>Czas oceny (strefa lokalna) — kolumna „Ocena” w historii pobytu.</summary>
    public string AssessedAtDisplay => AssessedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Wynik operacji zapisu zlecenia lekarskiego.</summary>
public sealed record MedicalOrderDto(
    Guid Id,
    MedicalOrderType Type,
    string Description,
    MedicalOrderState State,
    bool IsUrgent,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? CancellationReason)
{
    /// <summary>Czas zlecenia (strefa lokalna) — kolumna „Zlecono” w historii pobytu.</summary>
    public string OrderedAtDisplay => OrderedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    /// <summary>Czas realizacji (strefa lokalna); „—” dla zlecenia niezrealizowanego.</summary>
    public string CompletedAtDisplay => CompletedAtUtc is null
        ? "—"
        : CompletedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Wpis historii przeniesień pacjenta między strefami.</summary>
public sealed record ZoneTransferDto(
    Guid Id,
    string FromZoneName,
    string ToZoneName,
    string Reason,
    DateTimeOffset TransferredAtUtc,
    bool IsInitialAssignment)
{
    /// <summary>Czas przeniesienia (strefa lokalna) — kolumna w historii pobytu.</summary>
    public string TransferredAtDisplay => TransferredAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Wpis wypisu pacjenta z SOR w karcie pacjenta (BR-11).</summary>
public sealed record PatientDischargeDto(
    Guid Id,
    DischargeType Type,
    string? DepartmentName,
    string? Reason,
    DateTimeOffset DischargedAtUtc,
    string RecordedByLogin)
{
    /// <summary>Czas wypisu (strefa lokalna) — kolumna w historii pobytu.</summary>
    public string DischargedAtDisplay => DischargedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>Pozycja katalogu oddziałów — cel przekazania pacjenta z SOR.</summary>
public sealed record DepartmentDto(Guid Id, string Code, string Name);