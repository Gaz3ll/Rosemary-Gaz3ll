using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>Wynik monitoringu obciążenia pojedynczej strefy — podstawa komponentu pulpitu.</summary>
public sealed record ZoneLoadDto(
    Guid ZoneId,
    string ZoneCode,
    string ZoneName,
    ZoneKind Kind,
    decimal LoadRatio,
    ZoneLoadStatus Status,
    int ActivePatientCount,
    int StaffCount,
    int Capacity,
    int FreeBeds,
    bool IsCurrentUserZone,
    string StatusDescription);

/// <summary>Zestaw wyników monitoringu dla całego oddziału.</summary>
public sealed record DepartmentLoadSnapshotDto(
    IReadOnlyList<ZoneLoadDto> Zones,
    DateTimeOffset EvaluatedAtUtc)
{
    /// <summary>Czy w oddziale występuje co najmniej jedna strefa wymagająca interwencji.</summary>
    public bool HasCriticalZone => Zones.Any(z => z.Status >= ZoneLoadStatus.Warning);

    /// <summary>Strefa o najwyższym obciążeniu (źródło priorytetowego banera).</summary>
    public ZoneLoadDto? MostLoadedZone =>
        Zones.OrderByDescending(z => z.LoadRatio).FirstOrDefault();
}

/// <summary>Opis pojedynczego przypisania personelu do strefy (tablica personelu).</summary>
public sealed record StaffAssignmentDto(
    Guid UserId,
    string Login,
    string DisplayName,
    UserRole Role,
    Guid ZoneId,
    string ZoneName,
    ReassignmentKind AssignmentKind,
    string ReasonDescription,
    DateTimeOffset EffectiveFromUtc,
    int ActivePatientCount,
    bool IsCurrentUser);

/// <summary>Wpis historii rotacji wyświetlany w szczegółach personelu.</summary>
public sealed record RotationHistoryDto(
    Guid AssignmentId,
    Guid UserId,
    string UserDisplayName,
    ReassignmentKind Kind,
    string FromZoneName,
    string ToZoneName,
    string ReasonCode,
    string ReasonComment,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsCurrent);