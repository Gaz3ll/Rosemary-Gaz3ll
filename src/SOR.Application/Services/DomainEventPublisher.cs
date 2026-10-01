using System.Collections.Concurrent;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;

namespace SOR.Application.Services;

/// <summary>
/// Domyślna implementacja publikatora zdarzeń domenowych — buforuje zdarzenia w obrębie
/// bieżącego zakresu (scope). Warstwa prezentacja może je odczytać i przekazać do systemu
/// zewnętrznego, a testy mogą zweryfikować, że zdarzenia faktycznie zostały zgłoszone.
///
/// Kolekcja jest wątkozabezpieczna, ponieważ pętla monitoringu publikuje z wątku roboczego.
/// </summary>
public sealed class DomainEventPublisher : IDomainEventPublisher
{
    private readonly ConcurrentQueue<DomainEvent> _events = new();

    /// <inheritdoc />
    public IReadOnlyList<DomainEvent> PublishedEvents => _events.ToArray();

    /// <inheritdoc />
    public void PublishZoneOverloaded(ZoneLoadDto zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var @event = new ZoneOverloadedEvent
        {
            ZoneId = zone.ZoneId,
            ZoneName = zone.ZoneName,
            LoadRatio = zone.LoadRatio,
            ActivePatientCount = zone.ActivePatientCount,
            StaffCount = zone.StaffCount,
            StatusDescription = zone.StatusDescription
        };

        @event.SetAggregateIdentity(zone.ZoneId);
        _events.Enqueue(@event);
    }

    /// <inheritdoc />
    public void PublishZoneNormalized(ZoneLoadDto zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var @event = new ZoneLoadNormalizedEvent
        {
            ZoneId = zone.ZoneId,
            ZoneName = zone.ZoneName,
            LoadRatio = zone.LoadRatio
        };

        @event.SetAggregateIdentity(zone.ZoneId);
        _events.Enqueue(@event);
    }

    /// <inheritdoc />
    public void PublishStaffReassigned(StaffZoneReassignedEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        @event.SetAggregateIdentity(@event.UserId);
        _events.Enqueue(@event);
    }
}