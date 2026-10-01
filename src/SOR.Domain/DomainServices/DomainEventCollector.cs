using SOR.Domain.Common;

namespace SOR.Domain.DomainServices;

/// <summary>
/// Mechanizm publikacji zdarzeń domenowych (Mediator / Event Aggregator).
/// Warstwa domeny nie zna warstwy prezentacji — zdarzenia są przekazywane przez tę abstrakcję.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Asynchronicznie publikuje zdarzenie do wszystkich subskrybentów.</summary>
    Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Kolektor zdarzeń domenowych. Subskrypcja jest silnie typowana
/// (żadna konwersja typów na warstwie prezentacji), a opóźnienie propagacji zapewnia
/// natychmiastową reakcję interfejsu po zatwierdzeniu transakcji.
/// </summary>
public sealed class DomainEventCollector : IDomainEventDispatcher
{
    private readonly List<ISubscriber> _subscribers = new();
    private readonly object _syncRoot = new();

    /// <summary>Podpięcie zdarzenia do typu <c>TEvent</c>.</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
        where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscriber = new TypedSubscriber<TEvent>(handler);

        lock (_syncRoot)
        {
            _subscribers.Add(subscriber);
        }

        return new SubscriptionToken(() =>
        {
            lock (_syncRoot)
            {
                _subscribers.Remove(subscriber);
            }
        });
    }

    public Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        List<ISubscriber> snapshot;
        lock (_syncRoot)
        {
            snapshot = _subscribers.ToList();
        }

        return Task.WhenAll(snapshot.Select(s => s.HandleAsync(domainEvent, cancellationToken)));
    }

    private interface ISubscriber
    {
        Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken);
    }

    private sealed class TypedSubscriber<TEvent> : ISubscriber where TEvent : DomainEvent
    {
        private readonly Func<TEvent, Task> _handler;

        public TypedSubscriber(Func<TEvent, Task> handler) => _handler = handler;

        public Task HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken)
        {
            if (domainEvent is TEvent typed)
            {
                return _handler(typed);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Token anulowania subskrypcji (wzorzec Observer z IDisposable).</summary>
    private sealed class SubscriptionToken : IDisposable
    {
        private Action? _unsubscribe;

        public SubscriptionToken(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}