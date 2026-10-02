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

/// <summary>Pełny widok karty pacjenta (okno szczegółów).</summary>
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
    IReadOnlyList<MedicalOrderDto> Orders,
    IReadOnlyList<ZoneTransferDto> Transfers,
    IReadOnlyList<MedicationAdministrationDto> Administrations,
    IReadOnlyList<string> ClosureBlockers);

/// <summary>Wynik operacji zapisu zlecenia lekarskiego.</summary>
public sealed record MedicalOrderDto(
    Guid Id,
    MedicalOrderType Type,
    string Description,
    MedicalOrderState State,
    bool IsUrgent,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? CancellationReason);

/// <summary>Wpis historii przeniesień pacjenta między strefami.</summary>
public sealed record ZoneTransferDto(
    Guid Id,
    string FromZoneName,
    string ToZoneName,
    string Reason,
    DateTimeOffset TransferredAtUtc,
    bool IsInitialAssignment);