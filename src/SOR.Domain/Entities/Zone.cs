using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Agregat <c>Zone</c> reprezentuje fizyczny obszar SOR. Tożsamość wynika z kodu strefy (kod domenowy),
/// który jest jednocześnie stabilnym kluczem w grafiku i w dzienniku audytu.
/// </summary>
public sealed class Zone : Entity<Guid>
{
    private readonly List<DutyShift> _dutyShifts = new();
    private readonly List<StaffZoneAssignment> _staffAssignments = new();

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private Zone() { }
    private Zone(Guid id, string code, string name, ZoneKind kind, int capacity)
    {
        Id = id;
        Code = code;
        Name = name;
        Kind = kind;
        Capacity = capacity;
    }

    /// <summary>Kod domenowy strefy (BR-01): <c>TRI</c>, <c>EMG</c>, <c>INT</c>, <c>TRM</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Nazwa wyświetlana strefy.</summary>
    public string Name { get; private set; }

    /// <summary>Typ strefy — steruje regułami i kolorystyką pulpitu.</summary>
    public ZoneKind Kind { get; private set; }

    /// <summary>Zdolność przyjęciowa strefy (liczba stanowisk/łóżek).</summary>
    public int Capacity { get; private set; }

    /// <summary>Strefa jest aktywna — może przyjmować pacjentów i personel.</summary>
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<DutyShift> DutyShifts => _dutyShifts.AsReadOnly();

    public IReadOnlyCollection<StaffZoneAssignment> StaffAssignments => _staffAssignments.AsReadOnly();

    /// <summary>Fabryka tworząca strefę z pełną walidacją BR-01/BR-02.</summary>
    public static Zone Create(Guid id, string code, string name, ZoneKind kind, int capacity)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator strefy jest wymagany.", nameof(id));
        }

        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalizedCode.Length != 3)
        {
            throw new ValidationException("Kod strefy musi składać się z 3 znaków (BR-01).", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Nazwa strefy jest wymagana (BR-02).", nameof(name));
        }

        if (capacity <= 0)
        {
            throw new ValidationException("Zdolność strefy musi być dodatnia (BR-03).", nameof(capacity));
        }

        return new Zone(id, normalizedCode, name.Trim(), kind, capacity);
    }

    /// <summary>Reguła BR-04: strefa nie przyjmuje pacjentów powyżej zdolności przyjęciowej.</summary>
    public void EnsureCanAcceptPatient(int currentActivePatientCount)
    {
        if (!IsActive)
        {
            throw new ValidationException($"Strefa '{Name}' jest nieaktywna i nie przyjmuje pacjentów (BR-04).", nameof(currentActivePatientCount));
        }

        if (currentActivePatientCount >= Capacity)
        {
            throw new ValidationException(
                $"Strefa '{Name}' osiągnęła zdolność przyjęciową ({Capacity}) — brak wolnych miejsc (BR-04).",
                nameof(currentActivePatientCount));
        }
    }

    /// <summary>Określa, czy strefa jest strefą docelową dla rotacji (strefa aktywna).</summary>
    public bool CanAcceptRotation() => IsActive;

    /// <summary>Zmiana zdolności przyjęciowej — operacja administracyjna.</summary>
    public void ChangeCapacity(int newCapacity)
    {
        if (newCapacity <= 0)
        {
            throw new ValidationException("Zdolność strefy musi być dodatnia (BR-03).", nameof(newCapacity));
        }

        Capacity = newCapacity;
    }

    /// <summary>Deaktywacja strefy (np. trwa remont gabinetu zabiegowego).</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    // Wywołania używane przez warstwę trwałości (EF Core) w celu odbudowy nawigacji kolekcji.
    internal void AttachDutyShift(DutyShift shift) => _dutyShifts.Add(shift);

    internal void AttachStaffAssignment(StaffZoneAssignment assignment) => _staffAssignments.Add(assignment);
}
