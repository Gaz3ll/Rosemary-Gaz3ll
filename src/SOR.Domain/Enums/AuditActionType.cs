namespace SOR.Domain.Enums;

/// <summary>Kody zdarzeń dziennika audytu — kompletna lista akcji śledzonych w systemie.</summary>
public enum AuditActionType
{
    /// <summary>Próba uwierzytelnienia (sukces lub niepowodzenie).</summary>
    LoginAttempt = 1,

    /// <summary>Udany logout.</summary>
    Logout = 2,

    /// <summary>Automatyczne dopasowanie strefy z grafiku po zalogowaniu.</summary>
    ZoneContextAssignedFromRoster = 3,

    /// <summary>Manualna zmiana strefy przez użytkownika.</summary>
    ZoneReassignedManually = 4,

    /// <summary>Akceptacja rekomendacji rotacji (kliknięcie „Przejdź i wesprzyj strefę”).</summary>
    RotationRecommendationAccepted = 5,

    /// <summary>Rejestracja pacjenta w module wstępnym.</summary>
    PatientRegistered = 6,

    /// <summary>Wykonanie oceny Triage.</summary>
    TriageAssessed = 7,

    /// <summary>Zmiana przypisania pacjenta do strefy.</summary>
    PatientZoneChanged = 8,

    /// <summary>Utworzenie zlecenia lekarskiego.</summary>
    OrderCreated = 9,

    /// <summary>Zmiana statusu zlecenia.</summary>
    OrderStatusChanged = 10,

    /// <summary>Próba zamknięcia karty pacjenta (sukces lub odrzucenie).</summary>
    PatientCardClosureAttempt = 11,

    /// <summary>Wykryto przekroczenie progu obciążenia strefy.</summary>
    ZoneOverloadDetected = 12,

    /// <summary>Wygenerowano rekomendację rotacji personelu.</summary>
    RotationRecommendationGenerated = 13,

    /// <summary>Odrzucono konflikt współbieżnej modyfikacji pacjenta.</summary>
    ConcurrentModificationBlocked = 14
}