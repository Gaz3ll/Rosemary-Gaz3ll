namespace SOR.Domain.Common;

/// <summary>
/// Zdarzenie domenowe. Zdarzenia są deklaratywnym nośnikiem faktów wewnątrz agregatu
/// i publikowane są na zewnątrz przez <c>IDomainEventDispatcher</c>.
/// </summary>
public abstract record DomainEvent
{
    protected DomainEvent()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Identyfikator zdarzenia (korelacja logów i diagnostyki).</summary>
    public Guid EventId { get; } = Guid.NewGuid();

    /// <summary>Identyfikator agregatu, który zgłosił zdarzenie.</summary>
    public Guid AggregateId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Nazwa typu zdarzenia używana w dzienniku audytu.</summary>
    public abstract string EventName { get; }

    public void SetAggregateIdentity(object aggregateId)
    {
        if (aggregateId is Guid guid && guid != Guid.Empty)
        {
            AggregateId = guid;
        }
    }
}

/// <summary>
/// Zdarzenie informujące o przekroczeniu krytycznego progu obciążenia strefy.
/// Używa typu <c>record</c>, aby umożliwić porównanie wartościowe w testach.
/// </summary>
public sealed record ZoneOverloadedEvent : DomainEvent
{
    public required Guid ZoneId { get; init; }

    public required string ZoneName { get; init; }

    /// <summary>Wyliczony wskaźnik obciążenia (pacjenci ważeni na lekarza).</summary>
    public required decimal LoadRatio { get; init; }

    public required int ActivePatientCount { get; init; }

    public required int StaffCount { get; init; }

    public required string StatusDescription { get; init; }

    public override string EventName => nameof(ZoneOverloadedEvent);
}

/// <summary>Zdarzenie zgłaszane po zakończeniu monitoringu, gdy obciążenie strefy spadło poniżej progu.</summary>
public sealed record ZoneLoadNormalizedEvent : DomainEvent
{
    public required Guid ZoneId { get; init; }

    public required string ZoneName { get; init; }

    public required decimal LoadRatio { get; init; }

    public override string EventName => nameof(ZoneLoadNormalizedEvent);
}

/// <summary>Zdarzenie zgłaszane po udokumentowanej zmianie strefy przez użytkownika.</summary>
public sealed record StaffZoneReassignedEvent : DomainEvent
{
    public required Guid AssignmentId { get; init; }

    public required Guid UserId { get; init; }

    public required string UserLogin { get; init; }

    public required Guid FromZoneId { get; init; }

    public required Guid ToZoneId { get; init; }

    public required string ReasonCode { get; init; }

    public override string EventName => nameof(StaffZoneReassignedEvent);
}

/// <summary>Zdarzenie zgłaszane po przeniesieniu pacjenta między strefami.</summary>
public sealed record PatientTransferredEvent : DomainEvent
{
    public required Guid TransferId { get; init; }

    public required Guid PatientId { get; init; }

    public required Guid FromZoneId { get; init; }

    public required Guid ToZoneId { get; init; }

    public override string EventName => nameof(PatientTransferredEvent);
}