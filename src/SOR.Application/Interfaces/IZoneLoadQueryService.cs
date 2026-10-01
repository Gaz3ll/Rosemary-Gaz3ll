using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Interfaces;

/// <summary>
/// Odczytowe źródło stanu obciążenia stref.
///
/// Wydzielenie tego kontraktu od <see cref="IZoneLoadMonitoringService"/> ma cel architektoniczny:
/// monitoring publikuje zdarzenia i uruchamia pętlę w tle (potrzebuje silnika rekomendacji oraz
/// dziennika audytu), natomiast serwis rotacji potrzebuje wyłącznie samego odczytu obciążenia.
/// Dzięki rozdzieleniu obu zależności graf obiektów pozostaje acykliczny — bez konieczności
/// stosowania wyjątkowości typu <c>Lazy&lt;T&gt;</c> w kontenerze DI.
/// </summary>
public interface IZoneLoadQueryService
{
    /// <summary>Buduje bieżący stan obciążenia wszystkich aktywnych stref oddziału.</summary>
    Task<DepartmentLoadSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Buduje stan obciążenia jednej strefy.</summary>
    Task<ZoneLoadDto> GetZoneLoadAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zwraca aktualną rekomendację rotacji wygenerowaną przez silnik rekomendacji.
    /// Nie publikuje zdarzeń i nie zapisuje audytu — wyłącznie oblicza propozycję.
    /// </summary>
    Task<RotationRecommendationDto?> GetRecommendationAsync(CancellationToken cancellationToken = default);
}