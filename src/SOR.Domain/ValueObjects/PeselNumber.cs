using System.Globalization;
using SOR.Domain.Common;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Numer PESEL jako obiekt wartościowy (BR-18).
///
/// <para>
/// Walidacja obejmuje trzy niezależne warstwy, zgodnie z formatem zdefiniowanym w rozporządzeniu
/// w sprawie dokumentów osobistych:
/// </para>
/// <list type="number">
/// <item>11 cyfr — brak liter, spacji i znaków rozdzielających;</item>
/// <item>poprawna suma kontrolna modulo 11 (wagi <c>1,3,7,9,1,3,7,9,1,3</c>);</item>
/// <item>poprawna data urodzenia zakodowana w pierwszych sześciu cyfrach.</item>
/// </list>
///
/// <para>
/// Zakodowany miesiąc niesie informację o płci i stuleciu: jest zapisany z przesunięciem
/// o 20 (kobiety XX i mężczyźni XXI wieku), 40 (mężczyźni XX i kobiety XXI wieku),
/// 60 lub 80 (dalsze stulecia). Zakresy przesunięcia pokrywają się między stuleciami,
/// więc numer sam w sobie nie rozstrzyga, czy czytamy XX czy XXI wiek — stulecie ustala
/// dopasowanie do daty zadeklarowanej w formularzu (<see cref="MatchesDateOfBirth"/>).
/// </para>
///
/// <para>
/// Płeć koduje jednoznacznie dziesiąta cyfra numeru seryjnego: parzysta oznacza mężczyznę,
/// nieparzysta kobietę. Walidacja nie odrzuca prawidłowych numerów osób urodzonych
/// w różnych stuleciach, a jednocześnie odrzuca daty nieistniejące (np. 30 lutego).
/// </para>
/// </summary>
public sealed class PeselNumber : ValueObject
{
    /// <summary>Wagi użyte do obliczania cyfry kontrolnej.</summary>
    private static readonly int[] Weights = { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };

    private PeselNumber(string value, DateOnly dateOfBirth, GenderEncoded gender)
    {
        Value = value;
        DateOfBirth = dateOfBirth;
        Gender = gender;
    }

    /// <summary>Znormalizowany numer PESEL (11 cyfr).</summary>
    public string Value { get; }

    /// <summary>Data urodzenia odczytana z numeru PESEL (stulecie ustalane heurystycznie).</summary>
    public DateOnly DateOfBirth { get; }

    /// <summary>
    /// Płeć zakodowana w numerze (do weryfikacji zgodności z danymi zadeklarowanymi).
    /// Źródłem jest parzystość dziesiątej cyfry (parzysta — mężczyzna, nieparzysta — kobieta),
    /// a nie przesunięcie miesiąca: przesunięcie o 20 oznacza kobietę urodzoną w XX wieku
    /// albo mężczyznę urodzonego w XXI wieku, więc samo nie rozstrzyga płci.
    /// </summary>
    public GenderEncoded Gender { get; }

    /// <summary>
    /// Sprawdza zgodność z datą urodzenia podaną w formularzu bez rozstrzygania stulecia.
    /// Numer PESEL koduje tylko dwie cyfry roku, więc zakodowana data odpowiada każdemu
    /// stuleciu o zgodnych dwóch cyfrach roku — np. 21.06.1925 i 21.06.2025 mają ten sam zapis.
    /// </summary>
    /// <param name="dateOfBirth">Data urodzenia zadeklarowana przez użytkownika.</param>
    /// <returns><c>true</c>, gdy dzień, miesiąc i dwie cyfry roku się zgadzają.</returns>
    public bool MatchesDateOfBirth(DateOnly dateOfBirth)
    {
        if (!TryReadMonthAndDay(Value, out var month, out var day))
        {
            return false;
        }

        var twoDigitYear = int.Parse(Value.AsSpan(0, 2), CultureInfo.InvariantCulture);

        return dateOfBirth.Month == month
            && dateOfBirth.Day == day
            && dateOfBirth.Year % 100 == twoDigitYear;
    }

    /// <summary>
    /// Próba utworzenia obiektu wartościowego z surowego wejścia użytkownika.
    /// </summary>
    /// <param name="raw">Wprowadzony numer — spacje i myślniki są ignorowane.</param>
    /// <param name="pesel">Znormalizowany numer poprawny.</param>
    /// <returns><c>true</c>, gdy numer spełnia wszystkie kryteria walidacji.</returns>
    public static bool TryParse(string? raw, out PeselNumber? pesel)
    {
        pesel = null;

        var normalized = Normalize(raw);

        if (normalized is null)
        {
            return false;
        }

        if (!TryReadDateOfBirth(normalized, out var dateOfBirth))
        {
            return false;
        }

        if (!HasValidChecksum(normalized))
        {
            return false;
        }

        pesel = new PeselNumber(normalized, dateOfBirth, ReadGender(normalized));
        return true;
    }

    /// <summary>Walidacja z komunikatem błędu gotowym do pokazania użytkownikowi.</summary>
    /// <exception cref="ValidationException">Gdy numer jest nieprawidłowy.</exception>
    public static PeselNumber Create(string? raw)
    {
        var normalized = Normalize(raw);

        if (normalized is null)
        {
            throw new ValidationException("PESEL musi składać się z 11 cyfr (BR-18).", nameof(raw));
        }

        if (!TryReadDateOfBirth(normalized, out var dateOfBirth))
        {
            throw new ValidationException(
                $"PESEL '{normalized}' zawiera nieprawidłową datę urodzenia (BR-18).", nameof(raw));
        }

        if (!HasValidChecksum(normalized))
        {
            throw new ValidationException(
                $"PESEL '{normalized}' nie przechodzi walidacji sumy kontrolnej (BR-18).", nameof(raw));
        }

        return new PeselNumber(normalized, dateOfBirth, ReadGender(normalized));
    }

    /// <summary>Płeć zakodowana w numerze PESEL.</summary>
    public enum GenderEncoded
    {
        /// <summary>Płeć żeńska (dziesiąta cyfra nieparzysta).</summary>
        Female = 1,

        /// <summary>Płeć męska (dziesiąta cyfra parzysta).</summary>
        Male = 2
    }

    /// <summary>Usuwa spacje, myślniki i kropki; zwraca <c>null</c>, gdy po oczyszczeniu nie ma 11 znaków.</summary>
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[11];
        var length = 0;

        foreach (var character in raw)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '.')
            {
                continue;
            }

            if (!char.IsAsciiDigit(character) || length == 11)
            {
                return null;
            }

            buffer[length++] = character;
        }

        return length == 11 ? new string(buffer) : null;
    }

    /// <summary>
    /// Odczytuje datę urodzenia z sześciu pierwszych cyfr. Miesiąc z przesunięciem 20/40/60/80
    /// wskazuje płeć i potowę stulecia; rok powstaje po odjęciu przesunięcia.
    /// </summary>
    private static bool TryReadDateOfBirth(string value, out DateOnly dateOfBirth)
    {
        dateOfBirth = default;

        var twoDigitYear = int.Parse(value.AsSpan(0, 2), CultureInfo.InvariantCulture);

        if (!TryReadMonthAndDay(value, out var month, out var day))
        {
            return false;
        }

        var encodedMonth = int.Parse(value.AsSpan(2, 2), CultureInfo.InvariantCulture);

        var centuryShift = encodedMonth switch
        {
            >= 61 and <= 72 or >= 81 and <= 92 => -100,
            _ => 0
        };

        // Zakodowane stulecie: zakres 00–25 traktujemy jako bieżące/poprzednie (rok ≥ 26),
        // zakres 26–99 jako stulecie wcześniejsze (wiek < 20 lat). Bez tej reguły
        // poprawny numer osoby np. 26-letniej zostałby zinterpretowany jako 126-letni.
        // Wartość ta jest tylko przybliżeniem — rozstrzygające porównanie z datą
        // zadeklarowaną realizuje <see cref="MatchesDateOfBirth"/>, które nie zakłada stulecia.
        var year = centuryShift switch
        {
            -100 => 1800 + twoDigitYear,
            _ => twoDigitYear <= 25 ? 2000 + twoDigitYear : 1900 + twoDigitYear
        };

        if (!DateOnly.TryParseExact(
                $"{year:D4}-{month:D2}-{day:D2}",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateOfBirth))
        {
            return false;
        }

        // Odrzucenie daty w przyszłości (np. błąd w literaturze).
        return dateOfBirth <= DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Odczytuje miesiąc i dzień z pierwszych sześciu cyfr, zwalniając numer z przesunięcia
    /// zależnego od płci i stulecia. Sam numer nie pozwala rozstrzygnąć, czy kobieta urodziła
    /// się w XX, czy mężczyzna w XXI wieku — dlatego stulecie ustala osobno dopasowanie
    /// do daty zadeklarowanej w formularzu.
    /// </summary>
    private static bool TryReadMonthAndDay(string value, out int month, out int day)
    {
        month = 0;
        day = 0;

        var encodedMonth = int.Parse(value.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var encodedDay = int.Parse(value.AsSpan(4, 2), CultureInfo.InvariantCulture);

        var offset = encodedMonth switch
        {
            >= 1 and <= 12 => 0,
            >= 21 and <= 32 => 20,
            >= 41 and <= 52 => 40,
            >= 61 and <= 72 => 60,
            >= 81 and <= 92 => 80,
            _ => -1
        };

        if (offset < 0)
        {
            return false;
        }

        month = encodedMonth - offset;
        day = encodedDay;

        return month is >= 1 and <= 12 && day is >= 1 and <= 31;
    }

    private static bool HasValidChecksum(string value)
    {
        var sum = 0;

        for (var index = 0; index < 10; index++)
        {
            sum += (value[index] - '0') * Weights[index];
        }

        var remainder = sum % 11;
        var expected = remainder == 10 ? 0 : remainder;

        return expected == value[10] - '0';
    }

    private static GenderEncoded ReadGender(string value)
    {
        // Dziesiąta cyfra (indeks 9) jest parzysta dla mężczyzn i nieparzysta dla kobiet —
        // to jedyny zapis płci, który nie zależy od stulecia.
        var serial = value[9] - '0';

        return serial % 2 == 0 ? GenderEncoded.Male : GenderEncoded.Female;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
