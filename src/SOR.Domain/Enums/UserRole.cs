namespace SOR.Domain.Enums;

/// <summary>Rola użytkownika w systemie — determinuje zakres uprawnień kontekstowych.</summary>
public enum UserRole
{
    /// <summary>Lekarz / lekarz SOR — pełne uprawnienia kliniczne w strefie.</summary>
    Physician = 1,

    /// <summary>Pielęgniarka lub ratownik triage — triage, wykonanie zleceń, brak rozpoznawania.</summary>
    Nurse = 2,

    /// <summary>Koordynator / ordynator — nadzór nad grafikiem, rotacją i wszystkimi strefami.</summary>
    Coordinator = 3
}