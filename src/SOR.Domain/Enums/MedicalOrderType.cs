namespace SOR.Domain.Enums;

/// <summary>Typ zlecenia lekarskiego — wpływa na uprawnienia do jego zamknięcia.</summary>
public enum MedicalOrderType
{
    /// <summary>Badanie laboratoryjne.</summary>
    Lab = 1,

    /// <summary>Badanie obrazowe (RTG, TK, USG).</summary>
    Imaging = 2,

    /// <summary>Zabieg / procedura.</summary>
    Procedure = 3,

    /// <summary>Podanie leku.</summary>
    Medication = 4,

    /// <summary>Konsultacja specjalistyczna.</summary>
    Consultation = 5,

    /// <summary>Obserwacja hemodynamiczna.</summary>
    Observation = 6
}

/// <summary>Cykl życia zlecenia lekarskiego.</summary>
public enum MedicalOrderState
{
    /// <summary>Zlecenie otwarte — blokuje zamknięcie karty pacjenta.</summary>
    Open = 0,

    /// <summary>Zlecenie w trakcie realizacji.</summary>
    InProgress = 1,

    /// <summary>Zlecenie zrealizowane — nie blokuje zamknięcia karty.</summary>
    Completed = 2,

    /// <summary>Zlecenie anulowane przez lekarza — nie blokuje zamknięcia karty.</summary>
    Cancelled = 3
}