using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Agregat <c>DutyShift</c> — planowany dyżur przypisany do strefy na podstawie grafiku.
/// Po zalogowaniu system odczytuje aktywny wpis i ustawia domyślny kontekst strefy (BR-16).
/// </summary>
public sealed class DutyShift : Entity<Guid>
{
    private DutyShift(Guid id, Guid userId, Guid zoneId, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        Id = id;
        UserId = userId;
        ZoneId = zoneId;
        StartsAtUtc = startUtc;
        EndsAtUtc = endUtc;
        IsCancelled = false;
    }

    public Guid UserId { get; private set; }

    public Guid ZoneId { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    /// <summary>Odwołanie dyżuru — system nie uwzględnia go w dopasowaniu strefy po zalogowaniu.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>Uwagi koordynatora dotyczące dyżuru.</summary>
    public string? Notes { get; private set; }

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private DutyShift()
    {
    }

    /// <summary>Przypisanie personelu wynikające z tego grafiku (rotacja zaplanowana).</summary>
    public StaffZoneAssignment? StaffAssignment { get; private set; }

    public static DutyShift Create(Guid id, Guid userId, Guid zoneId, DateTimeOffset startUtc, DateTimeOffset endUtc, string? notes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator dyżuru jest wymagany.", nameof(id));
        }

        if (userId == Guid.Empty)
        {
            throw new ValidationException("Identyfikator pracownika jest wymagany.", nameof(userId));
        }

        if (zoneId == Guid.Empty)
        {
            throw new ValidationException("Identyfikator strefy jest wymagany.", nameof(zoneId));
        }

        if (endUtc <= startUtc)
        {
            throw new ValidationException("Godzina zakończenia dyżuru musi być późniejsza niż rozpoczęcie.", nameof(endUtc));
        }

        if (endUtc - startUtc > TimeSpan.FromHours(16))
        {
            throw new ValidationException("Pojedynczy dyżur nie może trwać dłużej niż 16 godzin.", nameof(endUtc));
        }

        return new DutyShift(id, userId, zoneId, startUtc, endUtc)
        {
            Notes = notes?.Trim()
        };
    }

    /// <summary>Czy dyżur jest aktywny w danym momencie (BR-15).</summary>
    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        !IsCancelled && nowUtc >= StartsAtUtc && nowUtc < EndsAtUtc;

    /// <summary>Odwołanie dyżuru przez koordynatora.</summary>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("Powód odwołania dyżuru jest wymagany.", nameof(reason));
        }

        IsCancelled = true;
        Notes = $"ODWOŁANY: {reason.Trim()}";
    }

    internal void LinkStaffAssignment(StaffZoneAssignment assignment) => StaffAssignment = assignment;

    public override string ToString() => $"Dyżur {StartsAtUtc:dd.MM HH:mm}–{EndsAtUtc:HH:mm}, strefa {ZoneId}";
}

/// <summary>
/// Grafik dyżurów jako osobny agregat przechowujący wpisy. Ułatwia wykrywanie konfliktów
/// grafiku (BR-17: brak podwójnego dyżuru w tej samej strefie).
/// </summary>
public sealed class DutyRoster : Entity<Guid>
{
    private readonly List<DutyShift> _shifts = new();

    private DutyRoster(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Name { get; private set; }

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private DutyRoster()
    {
    }

    public IReadOnlyCollection<DutyShift> Shifts => _shifts.AsReadOnly();

    public static DutyRoster Create(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator grafiku jest wymagany.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Nazwa grafiku jest wymagana.", nameof(name));
        }

        return new DutyRoster(id, name.Trim());
    }

    /// <summary>Dodaje dyżur po weryfikacji reguły BR-17 (brak nakładania się dyżurów w tej samej strefie).</summary>
    public DutyShift AddShift(DutyShift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);

        var conflict = _shifts.FirstOrDefault(existing =>
            existing.ZoneId == shift.ZoneId &&
            !existing.IsCancelled &&
            existing.StartsAtUtc < shift.EndsAtUtc &&
            shift.StartsAtUtc < existing.EndsAtUtc);

        if (conflict is not null)
        {
            throw new ValidationException(
                $"Konflikt grafiku: strefa ma już dyżur {conflict.StartsAtUtc:dd.MM HH:mm}–{conflict.EndsAtUtc:HH:mm} (BR-17).",
                nameof(shift));
        }

        _shifts.Add(shift);
        return shift;
    }

    /// <summary>Wyszukuje aktywny dyżur użytkownika w danym momencie — podstawa kontekstowego logowania.</summary>
    public DutyShift? FindActiveShiftFor(Guid userId, DateTimeOffset nowUtc) =>
        _shifts.FirstOrDefault(s => s.UserId == userId && s.IsActiveAt(nowUtc));

    internal void AttachShift(DutyShift shift) => _shifts.Add(shift);
}