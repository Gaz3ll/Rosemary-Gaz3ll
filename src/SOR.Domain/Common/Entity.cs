namespace SOR.Domain.Common;

/// <summary>
/// Baza dla encji domenowych. Hermetyzuje tożsamość, wersjonowanie (optimistic concurrency)
/// oraz kolejkę zdarzeń domenowych (Domain Events).
/// </summary>
public abstract class Entity<TId> where TId : notnull
{
    private readonly List<DomainEvent> _domainEvents = new();

    protected Entity()
    {
    }

    protected Entity(TId id)
    {
        if (EqualityComparer<TId>.Default.Equals(id, default))
        {
            throw new ValidationException("Identyfikator encji nie może być pusty.");
        }

        Id = id;
    }

    /// <summary>Tożsamość encji — nieprzechodni stan obiektu w dziedzinie.</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>Wersja encji — token współbieżności (w bazie: <c>RowVersion</c>).</summary>
    public byte[]? RowVersion { get; protected set; }

    /// <summary>
    /// Nadaje nową wartość tokenu współbieżności. Wywoływane przez warstwę trwałości bezpośrednio
    /// przed zapisem — SQLite nie potrafi samodzielnie generować wartości <c>rowversion</c>,
    /// dlatego token jest w całości zarządzany przez aplikację.
    /// </summary>
    internal void StampRowVersion(byte[] version)
    {
        ArgumentNullException.ThrowIfNull(version);
        RowVersion = version;
    }

    /// <summary>Czy encja jest jeszcze nowa (nieutrwalona).</summary>
    public bool IsTransient() => Id.Equals(default);

    /// <summary>Zdarzenia domenowe zgłoszone przez encję od ostatniego opróżnienia koleki.</summary>
    public IReadOnlyList<DomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        domainEvent.SetAggregateIdentity(Id);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Metoda współdzielona przez wszystkie encje — porównanie po tożsamości, nie po stanie.</summary>
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return GetType() == other.GetType() && Id.Equals(other.Id);
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}