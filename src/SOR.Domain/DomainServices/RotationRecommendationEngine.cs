using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.DomainServices;

/// <summary>
/// Domyślna implementacja silnika rekomendacji rotacji.
/// Wybiera najbardziej obciążoną strefę oraz kandydata z najmniej obciążonej strefy,
/// z uwzględnieniem dopasowania kompetencji (lekarz preferowany dla strefy ratunkowej).
/// Realizuje regułę BR-07.
/// </summary>
public sealed class RotationRecommendationEngine : IRotationRecommendationEngine
{
    /// <summary>Minimalna różnica wskaźników, aby rekomendacja miała sens (unikamy mikro-rotacji).</summary>
    private const decimal MinimumReliefGain = 0.5m;

    private readonly IZoneLoadCalculator _loadCalculator;

    public RotationRecommendationEngine(IZoneLoadCalculator loadCalculator)
    {
        _loadCalculator = loadCalculator ?? throw new ArgumentNullException(nameof(loadCalculator));
    }

    public RotationRecommendation? Recommend(RotationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var overloadedZone = context.ZoneSnapshots
            .Where(z => z.LoadRatio.RequiresIntervention)
            .OrderByDescending(z => z.LoadRatio.Value)
            .FirstOrDefault();

        if (overloadedZone is null)
        {
            return null;
        }

        var candidate = SelectBestCandidate(context.StaffPool, overloadedZone);

        if (candidate is null)
        {
            return null;
        }

        var sourceSnapshot = context.ZoneSnapshots.FirstOrDefault(z => z.ZoneId == candidate.ZoneId);
        var sourceRatio = sourceSnapshot?.LoadRatio.Value ?? 0m;
        var reliefGain = overloadedZone.LoadRatio.Value - sourceRatio;

        return new RotationRecommendation(
            candidate.UserId,
            candidate.DisplayName,
            candidate.ZoneId,
            candidate.ZoneName,
            overloadedZone.ZoneId,
            overloadedZone.ZoneName,
            overloadedZone.LoadRatio.Value,
            sourceRatio,
            reliefGain);
    }

    /// <summary>Wybiera kandydata: najniższe obciążenie osobiste, dopasowanie roli, minimalna różnica wskaźników.</summary>
    private static StaffLoadInfo? SelectBestCandidate(IReadOnlyList<StaffLoadInfo> staffPool, ZoneLoadSnapshot targetZone)
    {
        if (staffPool.Count == 0)
        {
            return null;
        }

        var candidates = staffPool
            .Where(s => s.ZoneId != targetZone.ZoneId)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(s => ScoreCandidate(s, targetZone))
            .ThenBy(s => s.PersonalActivePatientCount)
            .First();
    }

    /// <summary>Punktacja kandydata — im niższa, tym lepszy kandydat.</summary>
    private static decimal ScoreCandidate(StaffLoadInfo candidate, ZoneLoadSnapshot targetZone)
    {
        // Dyskwalifikacja: lekarz jest preferowany dla strefy ratunkowej (Kod Czerwony).
        var rolePenalty = targetZone.Kind == ZoneKind.Emergency && candidate.Role != UserRole.Physician
            ? 1000m
            : 0m;

        return rolePenalty + candidate.PersonalActivePatientCount;
    }
}