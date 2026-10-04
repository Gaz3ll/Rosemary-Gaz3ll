using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.DomainServices;

/// <summary>
/// Dane wejściowe walidacji wypisu pacjenta z SOR.
/// </summary>
/// <param name="PatientId">Identyfikator pacjenta.</param>
/// <param name="Type">Wybrany sposób zakończenia pobytu.</param>
/// <param name="HasIcd10Diagnosis">Czy w karcie widnieje rozpoznanie ICD-10.</param>
/// <param name="OpenOrderCount">Liczba zleceń lekarskich w stanie otwartym lub w trakcie.</param>
/// <param name="State">Aktualny stan pacjenta w przepływie klinicznym.</param>
/// <param name="HasTargetDepartment">Czy wskazano oddział przyjmujący (przekazanie).</param>
/// <param name="HasReason">Czy podano uzasadnienie wypisu.</param>
public sealed record PatientDischargeContext(
    Guid PatientId,
    DischargeType Type,
    bool HasIcd10Diagnosis,
    int OpenOrderCount,
    PatientState State,
    bool HasTargetDepartment,
    bool HasReason);

/// <summary>
/// Reguły wypisu pacjenta z SOR (BR-09, BR-10, BR-11). Zestaw jest wspólny dla trzech
/// sposobów zakończenia pobytu, ale wymagania różnią się zgodnie z realią kliniczną:
/// zakończenie leczenia wymaga zamkniętej dokumentacji, natomiast wypis na własne żądanie
/// musi być możliwy także wtedy, gdy leczenie nie zostało dokończone.
/// </summary>
public static class PatientDischargePolicy
{
    private static readonly IReadOnlyList<Func<PatientDischargeContext, string?>> Rules =
        new Func<PatientDischargeContext, string?>[]
        {
            c => c.State is PatientState.Closed or PatientState.TransferredOut
                ? "Pacjent został już wydany z SOR (BR-11)."
                : null,

            c => c.Type is DischargeType.TreatmentCompleted or DischargeType.TransferToDepartment
                ? c.HasIcd10Diagnosis
                    ? null
                    : "Brak rozpoznania wg ICD-10 — wypis pacjenta z SOR wymaga rozpoznania (BR-09)."
                : null,

            c => c.Type is DischargeType.TreatmentCompleted or DischargeType.TransferToDepartment
                ? c.OpenOrderCount == 0
                    ? null
                    : $"Karta posiada {c.OpenOrderCount} otwartych zleceń lekarskich — zakończ lub anuluj je przed wypisem (BR-10)."
                : null,

            c => c.Type == DischargeType.TransferToDepartment && !c.HasTargetDepartment
                ? "Nie wskazano oddziału, do którego przekazywany jest pacjent (BR-11)."
                : null,

            c => c.Type is DischargeType.AtPatientRequest or DischargeType.TransferToDepartment && !c.HasReason
                ? $"Wypis typu „{Describe(c.Type)}” wymaga uzasadnienia (BR-11)."
                : null,
        };

    /// <summary>Lista powodów blokujących wypis (pusta lista = wypis jest dozwolony).</summary>
    public static IReadOnlyList<string> GetBlockingReasons(PatientDischargeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Rules
            .Select(rule => rule(context))
            .Where(reason => reason is not null)
            .Cast<string>()
            .ToList();
    }

    /// <summary>Czy wypis pacjenta jest dozwolony.</summary>
    public static bool CanDischarge(PatientDischargeContext context) => GetBlockingReasons(context).Count == 0;

    /// <summary>Waliduje wypis i rzuca wyjątkiem domenowym <see cref="PatientDischargeBlockedException"/>.</summary>
    public static void EnsureCanDischarge(PatientDischargeContext context)
    {
        var reasons = GetBlockingReasons(context);

        if (reasons.Count > 0)
        {
            throw new PatientDischargeBlockedException(reasons.ToArray());
        }
    }

    private static string Describe(DischargeType type) => type switch
    {
        DischargeType.AtPatientRequest => "na własne żądanie",
        DischargeType.TransferToDepartment => "z przekazaniem na inny oddział",
        _ => "zakończenie leczenia"
    };
}