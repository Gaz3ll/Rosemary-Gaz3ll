using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.DomainServices;

/// <summary>
/// Dane wejściowe kalkulacji obciążenia jednej strefy: pacjenci aktywni, personel przypisany
/// oraz obecność pacjenta czerwonego oczekującego na pilną interwencję.
/// </summary>
public sealed record ZoneLoadContext(
    Guid ZoneId,
    string ZoneName,
    ZoneKind ZoneKind,
    IReadOnlyList<PatientLoadSnapshot> ActivePatients,
    int AssignedStaffCount,
    bool HasPendingRedPatient)
{
    /// <summary>Liczba pacjentów o priorytecie żółtym lub pomarańczowym — wejście dla reguły BR-06.</summary>
    public int YellowAndOrangeCount =>
        ActivePatients.Count(p => p.TriageCategory is TriageCategory.Yellow or TriageCategory.Orange);

    public int RedCount => ActivePatients.Count(p => p.TriageCategory == TriageCategory.Red);

    /// <summary>Tworzy kontekst z listy pacjentów, filtrując automatycznie pacjentów nieaktywnych.</summary>
    public static ZoneLoadContext Create(
        Guid zoneId,
        string zoneName,
        ZoneKind zoneKind,
        IEnumerable<PatientLoadSnapshot> patients,
        int assignedStaffCount,
        bool hasPendingRedPatient)
    {
        var active = patients.Where(p => p.IsActive).ToList();
        return new ZoneLoadContext(zoneId, zoneName, zoneKind, active, assignedStaffCount, hasPendingRedPatient);
    }
}

/// <summary>
/// Kontrakt kalkulatora obciążenia strefy. Implementacja domenowa jest niezależna od infrastruktury,
/// co umożliwia podmianę strategii ważenia bez wpływu na resztę systemu.
/// </summary>
public interface IZoneLoadCalculator
{
    LoadRatio Calculate(ZoneLoadContext context);
}