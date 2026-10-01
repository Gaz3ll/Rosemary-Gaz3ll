using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Progowe wartości kalkulatora obciążenia strefy (BR-06). Obiekt wartości z pełną walidacją,
/// dzięki czemu niedozwolone konfiguracje (odwrócone progi) są odrzucane przy starcie systemu.
/// </summary>
public sealed class ZoneLoadThresholds : ValueObject
{
    /// <summary>Domyślna konfiguracja zgodna z założeniami projektowymi.</summary>
    public static ZoneLoadThresholds Default => new(
        warningRatio: 3.0m,
        criticalRatio: 5.0m,
        overloadRatio: 8.0m,
        redPatientWithoutStaffIsCritical: true,
        minimumStaffCount: 1);

    public ZoneLoadThresholds(
        decimal warningRatio,
        decimal criticalRatio,
        decimal overloadRatio,
        bool redPatientWithoutStaffIsCritical = true,
        int minimumStaffCount = 1)
    {
        if (warningRatio <= 0)
        {
            throw new ValidationException("Próg ostrzeżenia musi być dodatni.", nameof(warningRatio));
        }

        if (criticalRatio <= warningRatio)
        {
            throw new ValidationException(
                "Próg krytyczny musi być wyższy od progu ostrzeżenia.", nameof(criticalRatio));
        }

        if (overloadRatio <= criticalRatio)
        {
            throw new ValidationException(
                "Próg przeciążenia musi być wyższy od progu krytycznego.", nameof(overloadRatio));
        }

        if (minimumStaffCount < 0)
        {
            throw new ValidationException("Minimalna liczba personelu nie może być ujemna.", nameof(minimumStaffCount));
        }

        WarningRatio = warningRatio;
        CriticalRatio = criticalRatio;
        OverloadRatio = overloadRatio;
        RedPatientWithoutStaffIsCritical = redPatientWithoutStaffIsCritical;
        MinimumStaffCount = minimumStaffCount;
    }

    /// <summary>Próg podwyższonego obciążenia (status <see cref="ZoneLoadStatus.Elevated"/>).</summary>
    public decimal WarningRatio { get; }

    /// <summary>Próg krytyczny — np. &gt; 5 pacjentów żółtych/pomarańczowych na jednego lekarza (BR-06).</summary>
    public decimal CriticalRatio { get; }

    /// <summary>Próg skrajnego przeciążenia wymagający natychmiastowej rotacji.</summary>
    public decimal OverloadRatio { get; }

    /// <summary>BR-06b: obecność pacjenta czerwonego bez wolnego personelu zawsze oznacza przeciążenie.</summary>
    public bool RedPatientWithoutStaffIsCritical { get; }

    /// <summary>Minimalna liczba personelu, przy której wskaźnik jest liczony (ochrona przed dzieleniem przez zero).</summary>
    public int MinimumStaffCount { get; }

    /// <summary>
    /// Mapuje wartość wskaźnika na status obciążenia. Osobna metoda domena, nie interfejsu,
    /// ponieważ decyzja należy do kontekstu strefy (BR-06).
    /// </summary>
    public ZoneLoadStatus ClassifyByRatio(decimal loadRatio)
    {
        if (loadRatio >= OverloadRatio)
        {
            return ZoneLoadStatus.Overloaded;
        }

        if (loadRatio >= CriticalRatio)
        {
            return ZoneLoadStatus.Warning;
        }

        return loadRatio >= WarningRatio ? ZoneLoadStatus.Elevated : ZoneLoadStatus.Optimal;
    }

    protected override IEnumerable<object?> GetEqualityComponents() =>
        new object?[]
        {
            WarningRatio, CriticalRatio, OverloadRatio,
            RedPatientWithoutStaffIsCritical, MinimumStaffCount
        };
}