using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>Dane wejściowe komendy manualnej zmiany strefy przez użytkownika.</summary>
public sealed record ReassignZoneCommand(
    Guid UserId,
    Guid TargetZoneId,
    ReassignmentReasonCode ReasonCode,
    string? ReasonComment);

/// <summary>Dane wejściowe komendy przyjęcia rekomendacji rotacji.</summary>
public sealed record AcceptRotationCommand(
    Guid UserId,
    Guid TargetZoneId,
    ReassignmentReasonCode ReasonCode,
    string? ReasonComment);

/// <summary>Wynik udanej zmiany strefy.</summary>
public sealed record ReassignmentResultDto(
    Guid AssignmentId,
    Guid UserId,
    string UserDisplayName,
    Guid FromZoneId,
    string FromZoneName,
    Guid ToZoneId,
    string ToZoneName,
    ReassignmentKind Kind,
    string ReasonDescription,
    DateTimeOffset EffectiveFromUtc,
    string AuditMessage);

/// <summary>Rekomendacja rotacji gotowa do prezentacji w interfejsie (baner / okno dialogowe).</summary>
public sealed record RotationRecommendationDto(
    Guid CandidateUserId,
    string CandidateDisplayName,
    Guid SourceZoneId,
    string SourceZoneName,
    Guid TargetZoneId,
    string TargetZoneName,
    decimal TargetLoadRatio,
    decimal SourceLoadRatio,
    decimal ReliefGain,
    string BannerText,
    DateTimeOffset GeneratedAtUtc);

/// <summary>Podsumowanie wykonanej operacji z informacją o wpisie audytowym.</summary>
public sealed record OperationResultDto(bool Success, string Message, Guid? AuditEntryId);