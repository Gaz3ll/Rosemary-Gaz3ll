using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.DomainServices;

/// <summary>
/// Domyślna implementacja kalkulatora obciążenia strefy.
/// Wylicza ważoną sumę pacjentów (wg kodów Triage) na liczbę przypisanego personelu
/// i klasyfikuje status obciążenia zgodnie z <see cref="ZoneLoadThresholds"/>.
/// Implementuje regułę BR-06 oraz warunek szczególny BR-06b.
/// </summary>
public sealed class ZoneLoadCalculator : IZoneLoadCalculator
{
    private readonly ZoneLoadThresholds _thresholds;

    public ZoneLoadCalculator(ZoneLoadThresholds thresholds)
    {
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
    }

    public LoadRatio Calculate(ZoneLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var weightedLoad = CalculateWeightedLoad(context);
        var effectiveStaff = ResolveEffectiveStaffCount(context);
        var ratio = effectiveStaff > 0 ? weightedLoad / effectiveStaff : weightedLoad;

        var status = _thresholds.ClassifyByRatio(ratio);
        var rationale = BuildRationale(context, ratio, status, weightedLoad, effectiveStaff);

        // BR-06b: pacjent czerwony bez wolnego personelu zawsze kwalifikuje strefę jako przeciążoną,
        // niezależnie od wyliczonego wskaźnika liczbowego.
        if (_thresholds.RedPatientWithoutStaffIsCritical && context.HasPendingRedPatient && context.AssignedStaffCount == 0)
        {
            status = ZoneLoadStatus.Overloaded;
            rationale = "Obecność pacjenta o Kodzie Czerwonym przy zerowym personelu — przeciążenie krytyczne (BR-06b).";
        }

        return LoadRatio.Create(ratio, context.ActivePatients.Count, context.AssignedStaffCount, status, rationale);
    }

    /// <summary>Suma wag pacjentów ważona kodami Triage.</summary>
    private static decimal CalculateWeightedLoad(ZoneLoadContext context) =>
        context.ActivePatients.Sum(p => TriagePolicy.For(p.TriageCategory).LoadWeight);

    private int ResolveEffectiveStaffCount(ZoneLoadContext context) =>
        Math.Max(context.AssignedStaffCount, _thresholds.MinimumStaffCount);

    private static string BuildRationale(
        ZoneLoadContext context,
        decimal ratio,
        ZoneLoadStatus status,
        decimal weightedLoad,
        int effectiveStaff)
    {
        var yellowOrangeInfo = context.YellowAndOrangeCount > 0
            ? $", w tym {context.YellowAndOrangeCount} pacjentów żółtych/pomarańczowych"
            : string.Empty;

        return $"Ważone obciążenie {weightedLoad:0.0} / personel {effectiveStaff} = {ratio:0.00}" +
               $" (pacjenci aktywni: {context.ActivePatients.Count}{yellowOrangeInfo}); status: {status}.";
    }
}