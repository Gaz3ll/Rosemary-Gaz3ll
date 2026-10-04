using SOR.Domain.Common;

namespace SOR.Domain.Entities;

/// <summary>
/// Pozycja katalogu oddziałów szpitalnych, do których SOR przekazuje pacjentów
/// (BR-11). Katalog jest danymi referencyjnymi — listę oddziałów ustala szpital,
/// a nie personel SOR.
/// </summary>
public sealed class Department : Entity<Guid>
{
    private Department() { }

    private Department(Guid id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    /// <summary>Krótki kod oddziału, np. <c>KCHIR</c> — używany w raportach i w logach.</summary>
    public string Code { get; private set; }

    /// <summary>Pełna nazwa kliniki lub oddziału.</summary>
    public string Name { get; private set; }

    public static Department Create(Guid id, string code, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator oddziału jest wymagany.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 16)
        {
            throw new ValidationException("Kod oddziału jest wymagany (max. 16 znaków).", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3)
        {
            throw new ValidationException("Nazwa oddziału jest wymagana (min. 3 znaki).", nameof(name));
        }

        return new Department(id, code.Trim().ToUpperInvariant(), name.Trim());
    }
}