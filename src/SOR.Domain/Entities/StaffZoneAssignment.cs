using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.Entities;

/// <summary>
/// Agregat <c>StaffZoneAssignment</c> — przypisanie pracownika do strefy wraz z pełną historią rotacji.
/// Kluczowy element dziedziny: każda zmiana strefy (z grafiku, manualna, rekomendowana, zlecona przez
/// koordynatora) jest zapisywana jako osobny wpis z przyczyną, dlatego historia jest w pełni odtwarzalna.
/// </summary>
public sealed class StaffZoneAssignment : Entity<Guid>
{
    private readonly List<StaffZoneAssignment> _history = new();

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private StaffZoneAssignment() { }
    private StaffZoneAssignment(
        Guid id,
        Guid userId,
        Guid zoneId,
        ReassignmentKind kind,
        ReassignmentReason? reason,
        DateTimeOffset effectiveFromUtc,
        Guid? supersedesId)
    {
        Id = id;
        UserId = userId;
        ZoneId = zoneId;
        Kind = kind;
        Reason = reason;
        EffectiveFromUtc = effectiveFromUtc;
        SupersedesAssignmentId = supersedesId;
    }

    /// <summary>Pracownik, którego dotyczy przypisanie.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Strefa, do której pracownik jest przypisany.</summary>
    public Guid ZoneId { get; private set; }

    /// <summary>Rodzaj przypisania (z grafiku / manualne / rekomendowane / zlecone / zwolnienie).</summary>
    public ReassignmentKind Kind { get; private set; }

    /// <summary>Uzasadnienie — wymagane dla manualnych zmian i rekomendacji (BR-05).</summary>
    public ReassignmentReason? Reason { get; private set; }

    /// <summary>Momentalna od kiedy przypisanie obowiązuje.</summary>
    public DateTimeOffset EffectiveFromUtc { get; private set; }

    /// <summary>Moment zakończenia przypisania (ustawiany przy zmianie strefy).</summary>
    public DateTimeOffset? EffectiveToUtc { get; private set; }

    /// <summary>Wpis, który ten przypisanie zastępuje — tworzy spójny łańcuch historii rotacji.</summary>
    public Guid? SupersedesAssignmentId { get; private set; }

    /// <summary>Czy przypisanie jest nadal obowiązujące.</summary>
    public bool IsActive => EffectiveToUtc is null;

    /// <summary>Historia wcześniejszych przypisań tego samego pracownika (najnowsze na początku).</summary>
    public IReadOnlyCollection<StaffZoneAssignment> History => _history.AsReadOnly();

    // ---------- Fabryki ----------

    /// <summary>Wyznaczenie strefy z grafiku przy starcie dyżuru (BR-16).</summary>
    public static StaffZoneAssignment FromRoster(Guid id, Guid userId, Guid zoneId, DateTimeOffset effectiveFromUtc) =>
        new(id, userId, zoneId, ReassignmentKind.ScheduledFromRoster, null, effectiveFromUtc, null);

    /// <summary>
    /// Manualna zmiana strefy przez użytkownika (BR-05). Wymaga uzasadnienia.
    /// </summary>
    public static StaffZoneAssignment Manual(
        Guid id,
        Guid userId,
        Guid zoneId,
        ReassignmentReason reason,
        DateTimeOffset effectiveFromUtc,
        Guid? supersedesId = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new StaffZoneAssignment(id, userId, zoneId, ReassignmentKind.ManualReassignment, reason, effectiveFromUtc, supersedesId);
    }

    /// <summary>Przyjęcie rekomendacji systemu dotyczącej rotacji (BR-07).</summary>
    public static StaffZoneAssignment Recommended(
        Guid id,
        Guid userId,
        Guid zoneId,
        ReassignmentReason reason,
        DateTimeOffset effectiveFromUtc,
        Guid? supersedesId = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new StaffZoneAssignment(id, userId, zoneId, ReassignmentKind.RecommendedRotation, reason, effectiveFromUtc, supersedesId);
    }

    /// <summary>Rotacja zlecona przez koordynatora oddziału (BR-08).</summary>
    public static StaffZoneAssignment OrderedByCoordinator(
        Guid id,
        Guid userId,
        Guid zoneId,
        ReassignmentReason reason,
        DateTimeOffset effectiveFromUtc,
        Guid? supersedesId = null)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new StaffZoneAssignment(id, userId, zoneId, ReassignmentKind.CoordinatorOrder, reason, effectiveFromUtc, supersedesId);
    }

    /// <summary>Zakończenie dyżuru / zwolnienie ze strefy.</summary>
    public static StaffZoneAssignment Release(Guid id, Guid userId, DateTimeOffset effectiveFromUtc, Guid? supersedesId = null) =>
        new(id, userId, Guid.Empty, ReassignmentKind.Release, null, effectiveFromUtc, supersedesId);

    // ---------- Zachowanie ----------

    /// <summary>Zamknięcie przypisania wyznaczone momentem <paramref name="endedAtUtc"/>.</summary>
    public void Close(DateTimeOffset endedAtUtc)
    {
        if (EffectiveToUtc is not null)
        {
            throw new ValidationException("Przypisanie zostało już zakończone.", nameof(endedAtUtc));
        }

        if (endedAtUtc < EffectiveFromUtc)
        {
            throw new ValidationException("Data zakończenia nie może być wcześniejsza od daty rozpoczęcia.", nameof(endedAtUtc));
        }

        EffectiveToUtc = endedAtUtc;
    }

    /// <summary>Podpięcie parametrów wymaganych do zgłoszenia zdarzenia po udanej zmianie strefy.</summary>
    public void RaiseReassignmentEvent(Guid userId, string userLogin, Guid fromZoneId)
    {
        RaiseDomainEvent(new StaffZoneReassignedEvent
        {
            AssignmentId = Id,
            UserId = userId,
            UserLogin = userLogin,
            FromZoneId = fromZoneId,
            ToZoneId = ZoneId,
            ReasonCode = Reason?.Code.ToString() ?? Kind.ToString()
        });
    }

    /// <summary>
    /// Reguły BR-05 — walidacja samego przypisania przed zapisaniem w dzienniku.
    /// Dla przypisań innych niż wyznaczenie z grafiku uzasadnienie jest obligatoryjne.
    /// </summary>
    public void EnsureReasonRequired()
    {
        var requiresReason = Kind is ReassignmentKind.ManualReassignment
            or ReassignmentKind.RecommendedRotation
            or ReassignmentKind.CoordinatorOrder;

        if (requiresReason && Reason is null)
        {
            throw new InvalidZoneReassignmentException(
                $"Przypisanie typu {Kind} wymaga podania uzasadnienia zmiany strefy (BR-05).");
        }
    }

    /// <summary>Reguła BR-01c: przypisanie musi wskazywać istniejącą strefę (poza zwolnieniem).</summary>
    public void EnsureZoneSpecified()
    {
        if (Kind != ReassignmentKind.Release && ZoneId == Guid.Empty)
        {
            throw new InvalidZoneReassignmentException("Przypisanie musi wskazywać strefę (BR-01c).");
        }
    }

    /// <summary>Opis zmiany do dziennika audytu.</summary>
    public string DescribeChange() => Kind switch
    {
        ReassignmentKind.ScheduledFromRoster => $"Wyznaczenie z grafiku ({EffectiveFromUtc:dd.MM.yyyy HH:mm})",
        ReassignmentKind.ManualReassignment => $"Manualna zmiana strefy: {Reason}",
        ReassignmentKind.RecommendedRotation => $"Przyjęta rekomendacja rotacji: {Reason}",
        ReassignmentKind.CoordinatorOrder => $"Rotacja zlecona przez koordynatora: {Reason}",
        ReassignmentKind.Release => $"Zwolnienie ze strefy ({EffectiveFromUtc:dd.MM.yyyy HH:mm})",
        _ => Kind.ToString()
    };

    internal void AttachHistory(StaffZoneAssignment previous) => _history.Add(previous);

    public override string ToString() =>
        $"{UserId} -> {ZoneId} ({Kind}) od {EffectiveFromUtc:dd.MM HH:mm}{(EffectiveToUtc is null ? ", aktywne" : $", do {EffectiveToUtc:dd.MM HH:mm}")}";
}
