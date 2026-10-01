using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Wskaźnik obciążenia strefy — wynik kalkulacji ważonej sumy pacjentów na liczbę personelu.
/// Obiekt wartości z klasyfikacją statusu i gotowym komunikatem diagnostycznym dla UI.
/// </summary>
public sealed class LoadRatio : ValueObject
{
    private LoadRatio(decimal value, int activePatients, int staffCount, ZoneLoadStatus status, string rationale)
    {
        Value = value;
        ActivePatientCount = activePatients;
        StaffCount = staffCount;
        Status = status;
        Rationale = rationale;
    }

    /// <summary>Ważona suma pacjentów na jednego przypisanego pracownika.</summary>
    public decimal Value { get; }

    /// <summary>Liczba pacjentów aktywnych w strefie (bez pacjentów zamkniętych).</summary>
    public int ActivePatientCount { get; }

    /// <summary>Liczba personelu aktualnie przypisanego do strefy.</summary>
    public int StaffCount { get; }

    public ZoneLoadStatus Status { get; }

    /// <summary>Opis przyczyny przyznania statusu (np. „Pacjent czerwony bez wolnego personelu”).</summary>
    public string Rationale { get; }

    /// <summary>Czy strefa wymaga interwencji rotacyjnej.</summary>
    public bool RequiresIntervention => Status >= ZoneLoadStatus.Warning;

    /// <summary>Fabryka wyniku kalkulacji — waliduje spójność składowych.</summary>
    public static LoadRatio Create(
        decimal weightedPatientLoad,
        int activePatientCount,
        int staffCount,
        ZoneLoadStatus status,
        string rationale)
    {
        if (weightedPatientLoad < 0)
        {
            throw new ValidationException("Ważone obciążenie nie może być ujemne.", nameof(weightedPatientLoad));
        }

        if (activePatientCount < 0)
        {
            throw new ValidationException("Liczba pacjentów nie może być ujemna.", nameof(activePatientCount));
        }

        if (staffCount < 0)
        {
            throw new ValidationException("Liczba personelu nie może być ujemna.", nameof(staffCount));
        }

        return new LoadRatio(weightedPatientLoad, activePatientCount, staffCount, status, rationale);
    }

    /// <summary>Buduje wskaźnik z prostej (nieważonej) liczby pacjentów — używane w testach i prezentacji.</summary>
    public static LoadRatio FromPlainCounts(int activePatientCount, int staffCount, ZoneLoadThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var effectiveStaff = Math.Max(staffCount, thresholds.MinimumStaffCount);
        var ratio = activePatientCount / (decimal)effectiveStaff;
        var status = thresholds.ClassifyByRatio(ratio);
        return Create(ratio, activePatientCount, staffCount, status, $"Prosty wskaźnik: {activePatientCount} pacjentów / {effectiveStaff} personelu.");
    }

    protected override IEnumerable<object?> GetEqualityComponents() =>
        new object?[] { Value, ActivePatientCount, StaffCount, Status };

    public override string ToString() => $"{Value:0.00} (pacjenci: {ActivePatientCount}, personel: {StaffCount}, status: {Status})";
}