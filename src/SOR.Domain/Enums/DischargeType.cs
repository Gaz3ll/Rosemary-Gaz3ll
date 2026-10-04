namespace SOR.Domain.Enums;

/// <summary>
/// Sposób zakończenia pobytu pacjenta w SOR (BR-11). Każdy wariant kończy obsługę
/// pacjenta w oddziale, ale pozostawia inny ślad w karcie: zakończenie leczenia i wypis
/// na własne żądanie zamykają kartę, przekazanie na inny oddział wyprowadza pacjenta
/// z SOR i wskazuje oddział przyjmujący.
/// </summary>
public enum DischargeType
{
    /// <summary>Zakończenie leczenia — pacjent wypisywany do domu po zakończonej terapii.</summary>
    TreatmentCompleted = 0,

    /// <summary>Wypis na własne żądanie — pacjent samodzielnie opuszcza SOR w trakcie leczenia.</summary>
    AtPatientRequest = 1,

    /// <summary>Przekazanie na inny oddział szpitalny w celu dalszego leczenia.</summary>
    TransferToDepartment = 2
}