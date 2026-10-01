namespace SOR.Domain.Enums;

/// <summary>Predefiniowane przyczyny manualnej zmiany strefy (wartości słownika <c>ReassignmentReason</c>).</summary>
public enum ReassignmentReasonCode
{
    /// <summary>Wsparcie resuscytacji / obszaru Kod Czerwony.</summary>
    ResuscitationSupport = 1,

    /// <summary>Nagłe zastępstwo nieobecnego pracownika.</summary>
    EmergencySubstitute = 2,

    /// <summary>Wzrost napływu pacjentów urazowych.</summary>
    TraumaSurge = 3,

    /// <summary>Wzrost napływu pacjentów internistycznych.</summary>
    InternalSurge = 4,

    /// <summary>Wsparcie triage — wzmożony napływ do punktu przyjęć.</summary>
    TriageSupport = 5,

    /// <summary>Koordynacja rozległego badania obrazowego.</summary>
    ImagingCoordination = 6,

    /// <summary>Przerwa / odpoczynek regulacyjny.</summary>
    RegulatedBreak = 7,

    /// <summary>Błąd lub zły stan techniczny stanowiska.</summary>
    EquipmentFailure = 8,

    /// <summary>Inny powód — wymaga wpisu w polu tekstowym.</summary>
    Other = 99
}