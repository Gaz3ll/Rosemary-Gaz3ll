using SOR.Domain.Common;

namespace SOR.Domain.Entities;

/// <summary>
/// Pozycja katalogu kodów ICD-10 — dane referencyjne wspierające kodowanie rozpoznań (BR-09).
///
/// <para>
/// Rola katalogu to weryfikacja poprawności wpisanego kodu oraz podpowiedź opisu. Sam fakt
/// istnienia kodu w katalogu nie jest wystarczający — rozpoznanie weryfikuje osoba
/// uprawniona (BR-13). Katalog obejmuje rozdziały najczęściej spotykane w SOR i oznaczony
/// jest flagą <see cref="IsEmergencyRelevant"/>, aby kodownicy mogli filtrować zbiór roboczy.
/// </para>
/// </summary>
public sealed class Icd10CatalogEntry : Entity<Guid>
{
    private Icd10CatalogEntry() { }

    private Icd10CatalogEntry(
        Guid id,
        string code,
        string description,
        string chapter,
        string? category,
        bool isEmergencyRelevant)
    {
        Id = id;
        Code = code;
        Description = description;
        Chapter = chapter;
        Category = category;
        IsEmergencyRelevant = isEmergencyRelevant;
    }

    /// <summary>Kod ICD-10 w formacie <c>X00</c> lub <c>X00.0</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Polski opis rozpoznania.</summary>
    public string Description { get; private set; }

    /// <summary>Rozdział ICD-10, np. „IX. Choroby układu krążenia".</summary>
    public string Chapter { get; private set; }

    /// <summary>Grupa/kategoria nadrzędna, np. „Krwawienia wewnętrzne".</summary>
    public string? Category { get; private set; }

    /// <summary>Czy rozpoznanie należy do typowego zakresu praktyki SOR.</summary>
    public bool IsEmergencyRelevant { get; private set; }

    public static Icd10CatalogEntry Create(
        Guid id,
        string code,
        string description,
        string chapter,
        string? category = null,
        bool isEmergencyRelevant = true)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator pozycji katalogu ICD-10 jest wymagany.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException("Kod ICD-10 jest wymagany.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationException("Opis rozpoznania ICD-10 jest wymagany.", nameof(description));
        }

        if (string.IsNullOrWhiteSpace(chapter))
        {
            throw new ValidationException("Rozdział ICD-10 jest wymagany.", nameof(chapter));
        }

        return new Icd10CatalogEntry(
            id,
            code.Trim().ToUpperInvariant(),
            description.Trim(),
            chapter.Trim(),
            string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            isEmergencyRelevant);
    }

    public override string ToString() => $"{Code} — {Description}";
}
