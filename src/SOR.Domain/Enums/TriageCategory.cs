namespace SOR.Domain.Enums;

/// <summary>
/// Pięciostopniowa skala segregacji medycznej (Triage) obowiązująca w SOR.
/// Wartości liczbowe odzwierciedlają rosnącą pilność i są używane do ważenia obciążenia strefy.
/// </summary>
public enum TriageCategory
{
    /// <summary>Kod Czerwony — stan bezpośredniego zagrożenia życia, leczenie natychmiastowe (0 min).</summary>
    Red = 1,

    /// <summary>Kod Pomarańczowy — stan bardzo pilny, leczenie do 10 minut.</summary>
    Orange = 2,

    /// <summary>Kod Żółty — stan pilny, maksymalny czas oczekiwania 60 minut.</summary>
    Yellow = 3,

    /// <summary>Kod Zielony — pacjent stabilny, przewidywany czas oczekiwania do 240 minut.</summary>
    Green = 4,

    /// <summary>Kod Niebieski — pacjent mało pilny, przewidywany czas oczekiwania do 480 minut.</summary>
    Blue = 5
}