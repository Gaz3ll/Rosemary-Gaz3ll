using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.DomainServices;

/// <summary>Zestaw reguł dla walidacji karty pacjenta przed jej zamknięciem.</summary>
public sealed record PatientClosureContext(
    Guid PatientId,
    bool HasIcd10Diagnosis,
    int OpenOrderCount,
    PatientState State,
    bool HasPendingTransportOrder);

/// <summary>Reguła BR-09 / BR-10 — warunki poprawności zamykania karty pacjenta.</summary>
public static class PatientCardClosurePolicy
{
    private static readonly IReadOnlyList<Func<PatientClosureContext, string?>> Rules = new Func<PatientClosureContext, string?>[]
    {
        c => c.HasIcd10Diagnosis
            ? null
            : "Brak rozpoznania wg ICD-10 — nie można zamknąć karty pacjenta (BR-09).",
        c => c.OpenOrderCount == 0
            ? null
            : $"Karta posiada {c.OpenOrderCount} otwartych zleceń lekarskich (BR-10).",
        c => c.State == PatientState.AwaitingTransport
            ? null
            : "Pacjent musi być w stanie oczekiwania na transport przed wydaniem z SOR (BR-11).",
        c => !c.HasPendingTransportOrder
            ? null
            : "Transport nie został zrealizowany — brak potwierdzenia przekazania pacjenta (BR-11)."
    };

    /// <summary>Zwraca listę powodów blokujących zamknięcie karty (pusta = można zamknąć).</summary>
    public static IReadOnlyList<string> GetBlockingReasons(PatientClosureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Rules.Select(rule => rule(context)).Where(reason => reason is not null).Cast<string>().ToList();
    }

    /// <summary>Czy kartę można zamknąć.</summary>
    public static bool CanClose(PatientClosureContext context) => GetBlockingReasons(context).Count == 0;

    /// <summary>Waliduje i rzuca wyjątkiem domenowym <see cref="PatientCardClosureBlockedException"/>.</summary>
    public static void EnsureCanClose(PatientClosureContext context)
    {
        var reasons = GetBlockingReasons(context);
        if (reasons.Count > 0)
        {
            throw new PatientCardClosureBlockedException(reasons.ToArray());
        }
    }
}