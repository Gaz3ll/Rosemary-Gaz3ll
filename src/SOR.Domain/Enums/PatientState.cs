namespace SOR.Domain.Enums;

/// <summary>Stan pacjenta w przepływie klinicznym SOR.</summary>
public enum PatientState
{
    /// <summary>Pacjent zarejestrowany w module wstępnym, oczekuje na Triage.</summary>
    Registered = 0,

    /// <summary>Przesegregowany, oczekuje na przydział do strefy.</summary>
    Triaged = 1,

    /// <summary>Przyjęty do strefy, trwa diagnostyka lub leczenie.</summary>
    InTreatment = 2,

    /// <summary>Przygotowywany do transportu poza SOR — łóżko zwalniane.</summary>
    AwaitingTransport = 3,

    /// <summary>Wydany do oddziału / oddziału chirurgicznego.</summary>
    TransferredOut = 4,

    /// <summary>Karta zamknięta — zakończona obsługa w SOR.</summary>
    Closed = 5
}