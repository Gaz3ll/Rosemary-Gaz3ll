using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Opisuje reguły biznesowe pojedynczego kodu Triage: maksymalny dopuszczalny czas oczekiwania
/// oraz wagę w obliczaniu obciążenia strefy (im wyższy kod, tym większe zużycie zasobów).
/// </summary>
public sealed class TriagePolicy : ValueObject
{
    private TriagePolicy(TriageCategory category, TimeSpan maxWaitTime, decimal loadWeight)
    {
        Category = category;
        MaxWaitTime = maxWaitTime;
        LoadWeight = loadWeight;
    }

    public TriageCategory Category { get; }

    /// <summary>Maksymalny czas oczekiwania na interwencję zgodny z wewnętrznymi procedurami SOR.</summary>
    public TimeSpan MaxWaitTime { get; }

    /// <summary>Waga pacjenta w kalkulatorze obciążenia strefy.</summary>
    public decimal LoadWeight { get; }

    /// <summary>Tablica reguł — jedyne źródło prawdy dla progów czasowych i wag.</summary>
    private static readonly IReadOnlyDictionary<TriageCategory, TriagePolicy> Policies =
        new Dictionary<TriageCategory, TriagePolicy>
        {
            [TriageCategory.Red] = new(TriageCategory.Red, TimeSpan.Zero, 8.0m),
            [TriageCategory.Orange] = new(TriageCategory.Orange, TimeSpan.FromMinutes(10), 4.0m),
            [TriageCategory.Yellow] = new(TriageCategory.Yellow, TimeSpan.FromMinutes(60), 2.0m),
            [TriageCategory.Green] = new(TriageCategory.Green, TimeSpan.FromMinutes(240), 1.0m),
            [TriageCategory.Blue] = new(TriageCategory.Blue, TimeSpan.FromMinutes(480), 0.5m)
        };

    /// <summary>Zwraca politykę dla wskazanego kodu Triage.</summary>
    public static TriagePolicy For(TriageCategory category) => Policies[category];

    /// <summary>Czy pacjent przekroczył dopuszczalny czas oczekiwania.</summary>
    public bool IsWaitingTimeExceeded(DateTimeOffset assignedAtUtc, DateTimeOffset nowUtc) =>
        nowUtc - assignedAtUtc > MaxWaitTime;

    /// <summary>Czy kod oznacza pacjenta wymagającego natychmiastowej interwencji.</summary>
    public bool IsImmediate() => Category is TriageCategory.Red or TriageCategory.Orange;

    protected override IEnumerable<object?> GetEqualityComponents() =>
        new object?[] { Category, MaxWaitTime, LoadWeight };

    /// <summary>Opis przyjazny dla warstwy prezentacji.</summary>
    public string Describe() => $"{Category} — maks. oczekiwanie {MaxWaitTime.TotalMinutes:0} min, waga {LoadWeight}";
}