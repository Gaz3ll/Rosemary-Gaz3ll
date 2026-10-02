using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Pakiety medyczne — gotowe zestawy postępowania dla typowych stanów nagłych.
///
/// <para>
/// Pakiet to wzorzec postępowania, a nie automatyczne zlecenie. Zastosowanie pakietu
/// materializuje jego pozycje w zleceniach lekarskich i rejestrze podanych leków, dlatego
/// każda zastosowana pozycja jest od tej chwili zwykłym, edytowalnym zleceniem — lekarz
/// może je anulować lub zmienić dawkę. Dzięki temu pakiet nie omija reguły BR-13.
/// </para>
/// </summary>
public sealed class MedicalBundle : Entity<Guid>
{
    private readonly List<MedicalBundleItem> _items = new();

    private MedicalBundle() { }

    private MedicalBundle(
        Guid id,
        string code,
        string name,
        string indication,
        string chapter)
    {
        Id = id;
        Code = code;
        Name = name;
        Indication = indication;
        Chapter = chapter;
    }

    /// <summary>Katalogowy kod pakietu, np. <c>PAK-SOR-003</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Nazwa pakietu, np. „Ból brzucha — diagnostyka i leczenie".</summary>
    public string Name { get; private set; }

    /// <summary>Wskazanie kliniczne, dla którego pakiet się sprawdza.</summary>
    public string Indication { get; private set; }

    /// <summary>Grupa tematyczna pakietów (np. „Bóle brzucha", „Urazy", „Zatrucia").</summary>
    public string Chapter { get; private set; }

    /// <summary>Pozycje składające się na pakiet, uporządkowane wg kolejności realizacji.</summary>
    public IReadOnlyCollection<MedicalBundleItem> Items => _items.AsReadOnly();

    public static MedicalBundle Create(Guid id, string code, string name, string indication, string chapter)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator pakietu jest wymagany.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException("Kod pakietu jest wymagany.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Nazwa pakietu jest wymagana.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(indication))
        {
            throw new ValidationException("Wskazanie kliniczne pakietu jest wymagane.", nameof(indication));
        }

        return new MedicalBundle(
            id,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            indication.Trim(),
            string.IsNullOrWhiteSpace(chapter) ? "Inne" : chapter.Trim());
    }

    /// <summary>Dodaje pozycję do pakietu — dozwolone wyłącznie przed zastosowaniem pakietu.</summary>
    public MedicalBundleItem AddItem(
        Guid itemId,
        int sequence,
        MedicalOrderType orderType,
        string description,
        MedicationRoute? route = null,
        Guid? medicationId = null,
        string? dose = null,
        bool isUrgent = false)
    {
        if (_items.Count > 0 && _items.Any(item => item.Sequence == sequence))
        {
            throw new ValidationException(
                $"Pozycja o numerze {sequence} już istnieje w pakiecie.", nameof(sequence));
        }

        var item = MedicalBundleItem.Create(
            itemId,
            Id,
            sequence,
            orderType,
            description,
            route,
            medicationId,
            dose,
            isUrgent);

        _items.Add(item);
        return item;
    }

    public override string ToString() => $"{Code}: {Name} ({_items.Count} poz.)";
}
