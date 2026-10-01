using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.DomainServices;

/// <summary>Rekomendacja rotacji wygenerowana przez system — kandydat i strefa docelowa.</summary>
public sealed record RotationRecommendation(
    Guid CandidateUserId,
    string CandidateDisplayName,
    Guid SourceZoneId,
    string SourceZoneName,
    Guid TargetZoneId,
    string TargetZoneName,
    decimal TargetLoadRatio,
    decimal SourceLoadRatio,
    decimal ReliefGain)
{
    /// <summary>Tekst gotowy do wyświetlenia w banerze rekomendacji.</summary>
    public string Describe() =>
        $"Strefa \u201e{TargetZoneName}\u201d jest przeci\u0105\u017cona (wska\u017cznik {TargetLoadRatio:0.00}). " +
        $"Zaproponowano wsparcie: {CandidateDisplayName} (obecnie \u201e{SourceZoneName}\u201d, wska\u017cznik {SourceLoadRatio:0.00}). " +
        $"Spodziewana poprawa: -{ReliefGain:0.00} pkt wska\u017cznika.";
}

/// <summary>Zestaw danych wejściowych dla silnika rekomendacji rotacji.</summary>
public sealed record RotationEvaluationContext(
    IReadOnlyList<StaffLoadInfo> StaffPool,
    IReadOnlyList<ZoneLoadSnapshot> ZoneSnapshots);

/// <summary>Obciążenie pojedyncego pracownika — wejście dla silnika rekomendacji.</summary>
public sealed record StaffLoadInfo(
    Guid UserId,
    string DisplayName,
    Guid ZoneId,
    string ZoneName,
    UserRole Role,
    int PersonalActivePatientCount);

/// <summary>Obciążenie strefy — wejście dla silnika rekomendacji.</summary>
public sealed record ZoneLoadSnapshot(
    Guid ZoneId,
    string ZoneName,
    ZoneKind Kind,
    LoadRatio LoadRatio);

/// <summary>Kontrakt silnika rekomendacji rotacji (Strategy).</summary>
public interface IRotationRecommendationEngine
{
    RotationRecommendation? Recommend(RotationEvaluationContext context);
}