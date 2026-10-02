using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Lek z katalogu SOR — pozycja katalogu możliwych do podania preparatów, a nie zlecenie.
///
/// <para>
/// Katalog jest danymi referencyjnymi: opisuje preparat dostępny w oddziale, typową dawkę
/// dla pacjenta dorosłego, maksymalną dawkę dobową oraz ostrzeżenia. Encja jest niemutowalna
/// po utworzeniu — zmiana formulariusza (np. wycofanie leku) realizowana jest przez
/// <see cref="Deactivate"/>, aby nie kasować historii podanych dawek.
/// </para>
/// </summary>
public sealed class Medication : Entity<Guid>
{
    private Medication() { }

    private Medication(
        Guid id,
        string code,
        string name,
        string form,
        string strength,
        MedicationCategory category,
        MedicationRoute route,
        string typicalDose,
        string maxDailyDose,
        MedicationSafety safety,
        bool isAvailable,
        string? contraindications,
        string? notes)
    {
        Id = id;
        Code = code;
        Name = name;
        Form = form;
        Strength = strength;
        Category = category;
        Route = route;
        TypicalDose = typicalDose;
        MaxDailyDose = maxDailyDose;
        Safety = safety;
        IsAvailable = isAvailable;
        Contraindications = contraindications;
        Notes = notes;
    }

    /// <summary>Katalogowy kod leku, np. <c>LEK-EMG-0012</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Międzynarodowa nazwa niezależna (INN) — nazwa substancji czynnej po polsku.</summary>
    public string Name { get; private set; }

    /// <summary>Postać leku, np. „ampułka", „tabletki", „worek 500 ml".</summary>
    public string Form { get; private set; }

    /// <summary>Moc preparatu, np. „500 mg/ml".</summary>
    public string Strength { get; private set; }

    public MedicationCategory Category { get; private set; }

    /// <summary>Droga podania zgodna z Charakterystyką Produktu leczniczego.</summary>
    public MedicationRoute Route { get; private set; }

    /// <summary>Typowa dawka jednorazowa dla pacjenta dorosłego (opis tekstowy).</summary>
    public string TypicalDose { get; private set; }

    /// <summary>Maksymalna dawka dobowa (opis tekstowy).</summary>
    public string MaxDailyDose { get; private set; }

    public MedicationSafety Safety { get; private set; }

    /// <summary>Czy preparat jest aktualnie dostępny w oddziale.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Przeciwwskazania i najważniejsze ostrzeżenia kliniczne.</summary>
    public string? Contraindications { get; private set; }

    /// <summary>Dodatkowe uwagi dotyczące podania w warunkach SOR.</summary>
    public string? Notes { get; private set; }

    /// <summary>Opis gotowy do wyświetlenia na liście wyboru.</summary>
    public string DisplayName => $"{Name} {Strength} — {Form}";

    public static Medication Create(
        Guid id,
        string code,
        string name,
        string form,
        string strength,
        MedicationCategory category,
        MedicationRoute route,
        string typicalDose,
        string maxDailyDose,
        MedicationSafety safety = MedicationSafety.Standard,
        bool isAvailable = true,
        string? contraindications = null,
        string? notes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator leku jest wymagany.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length is < 2 or > 32)
        {
            throw new ValidationException("Kód leku jest wymagany (2–32 znaki).", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Nazwa leku jest wymagana.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(form))
        {
            throw new ValidationException("Postać leku jest wymagana.", nameof(form));
        }

        if (string.IsNullOrWhiteSpace(strength))
        {
            throw new ValidationException("Moc preparatu jest wymagana.", nameof(strength));
        }

        if (string.IsNullOrWhiteSpace(typicalDose))
        {
            throw new ValidationException("Typowa dawka jest wymagana.", nameof(typicalDose));
        }

        if (string.IsNullOrWhiteSpace(maxDailyDose))
        {
            throw new ValidationException("Maksymalna dawka dobowa jest wymagana.", nameof(maxDailyDose));
        }

        return new Medication(
            id,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            form.Trim(),
            strength.Trim(),
            category,
            route,
            typicalDose.Trim(),
            maxDailyDose.Trim(),
            safety,
            isAvailable,
            Normalize(contraindications),
            Normalize(notes));
    }

    /// <summary>Wycofanie preparatu z formularza — nie usuwa historii podanych dawek.</summary>
    public void Deactivate() => IsAvailable = false;

    /// <summary>Przywrócenie preparatu do formularza.</summary>
    public void Activate() => IsAvailable = true;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public override string ToString() => $"{Code}: {DisplayName}";
}
