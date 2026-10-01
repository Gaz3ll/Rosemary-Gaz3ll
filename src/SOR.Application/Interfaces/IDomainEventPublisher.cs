using SOR.Application.DTOs;
using SOR.Domain.Common;

namespace SOR.Application.Interfaces;

/// <summary>
/// Punkt publikacji zdarzeń domenowych dla warstwy aplikacji.
///
/// Zdarzenia domenowe zgłaszane przez monitoring i rotację są przekazywane dalej jako strumień
/// faktów. Dzięki temu integracja z systemem zewnętrznym (kolejka komunikatów, webhook do HIS)
/// sprowadza się do podmiany implementacji tego interfejsu, bez ingerencji w logikę domenową.
/// </summary>
public interface IDomainEventPublisher
{
    /// <summary>Zdarzenie przekroczenia progu obciążenia strefy.</summary>
    void PublishZoneOverloaded(ZoneLoadDto zone);

    /// <summary>Zdarzenie normalizacji obciążenia strefy.</summary>
    void PublishZoneNormalized(ZoneLoadDto zone);

    /// <summary>Zdarzenie udokumentowanej zmiany strefy przez użytkownika.</summary>
    void PublishStaffReassigned(StaffZoneReassignedEvent @event);

    /// <summary>Wszystkie zdarzenia zgłoszone w bieżącej transakcji (przydatne w testach i diagnostyce).</summary>
    IReadOnlyList<DomainEvent> PublishedEvents { get; }
}