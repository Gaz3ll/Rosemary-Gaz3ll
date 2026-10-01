namespace SOR.Domain.Enums;

/// <summary>Kierunek zmiany przypisania personelu do strefy.</summary>
public enum ReassignmentKind
{
    /// <summary>Wyznaczenie strefy z grafiku przy rozpoczęciu dyżuru.</summary>
    ScheduledFromRoster = 0,

    /// <summary>Manualna zmiana strefy z podaniem przyczyny przez użytkownika.</summary>
    ManualReassignment = 1,

    /// <summary>Przyjęcie rekomendacji systemu dotyczącej rotacji.</summary>
    RecommendedRotation = 2,

    /// <summary>Przeniesienie podyktowane przez koordynatora oddziału.</summary>
    CoordinatorOrder = 3,

    /// <summary>Zakończenie dyżuru / zwolnienie ze strefy.</summary>
    Release = 4
}