using System.Globalization;
using System.Text.RegularExpressions;
using SOR.Domain.Common;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Kod rozpoznania wg klasyfikacji ICD-10. Obiekt wartości wymuszający poprawny format
/// (litera + 2 cyfry + opcjonalny kropkowy rozwinięcie), co realizuje regułę BR-09.
/// </summary>
public sealed partial class Icd10Code : ValueObject
{
    private Icd10Code(string normalized)
    {
        Value = normalized;
    }

    /// <summary>Znormalizowana postać kodu (bez spacji, wielkie litery).</summary>
    public string Value { get; }

    /// <summary>Litera kategorii rozdziału (np. <c>S</c> dla urazów, <c>I</c> dla chorób układu krążenia).</summary>
    public string ChapterLetter => Value[..1];

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core owned type).</summary>
    private Icd10Code()
    {
        Value = string.Empty;
    }

    /// <summary>Reguła BR-09: kod ma formę <c>X00</c> lub <c>X00.0</c>.</summary>
    [GeneratedRegex(@"^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Icd10Pattern();

    /// <summary>Tworzy obiekt wartości lub rzuca <see cref="ValidationException"/>.</summary>
    public static Icd10Code Create(string? raw)
    {
        var normalized = Normalize(raw);

        if (!Icd10Pattern().IsMatch(normalized))
        {
            throw new ValidationException(
                $"Kod ICD-10 '{raw}' jest nieprawidłowy. Oczekiwany format: X00 albo X00.0 (BR-09).",
                nameof(Icd10Code));
        }

        return new Icd10Code(normalized);
    }

    /// <summary>Próba utworzenia bez wyjątku — używana przy walidacji danych wejściowych z bazy.</summary>
    public static bool TryCreate(string? raw, out Icd10Code? code)
    {
        try
        {
            code = Create(raw);
            return true;
        }
        catch (ValidationException)
        {
            code = null;
            return false;
        }
    }

    /// <summary>Czy kod istnieje — używane w warunku kompletności karty pacjenta.</summary>
    public bool Exists => !string.IsNullOrWhiteSpace(Value);

    private static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpper(CultureInfo.InvariantCulture);

        return value;
    }

    protected override IEnumerable<object?> GetEqualityComponents() => new object?[] { Value };

    public override string ToString() => Value;
}