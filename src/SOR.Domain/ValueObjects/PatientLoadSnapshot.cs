using SOR.Domain.Enums;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Lekki, niemutowalny obraz pacjenta używany przez kalkulator obciążenia.
/// Celowo oddzielony od encji <c>Patient</c>, aby kalkulator nie zależał od warstwy trwałości.
/// </summary>
public sealed record PatientLoadSnapshot(
    Guid PatientId,
    string PatientName,
    TriageCategory TriageCategory,
    PatientState State,
    TimeSpan TimeInZone)
{
    /// <summary>Czy pacjent wlicza się do obciążenia strefy.</summary>
    public bool IsActive => State != PatientState.Closed && State != PatientState.TransferredOut;
}