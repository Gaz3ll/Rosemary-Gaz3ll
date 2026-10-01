namespace SOR.Domain.Enums;

/// <summary>
/// Status obciążenia strefy wyliczany przez kalkulator obciążenia.
/// Progi zgodne z parametrami konfiguracyjnymi obiektu <c>ZoneLoadThresholds</c>.
/// </summary>
public enum ZoneLoadStatus
{
    /// <summary>Obciążenie w normie — nie wymaga interwencji.</summary>
    Optimal = 0,

    /// <summary>Podwyższone obciążenie — system obserwuje strefę.</summary>
    Elevated = 1,

    /// <summary>Ostrzeżenie — przekroczony próg krytyczny, uruchomiona rekomendacja rotacji.</summary>
    Warning = 2,

    /// <summary>Przeciążenie krytyczne — pacjent czerwony bez wolnego personelu lub skrajny wskaźnik.</summary>
    Overloaded = 3
}