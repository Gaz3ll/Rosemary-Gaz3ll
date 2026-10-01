using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Agregat <c>User</c> — pracownik SOR. Przechowuje wyłącznie hash hasła (PBKDF2, sól per użytkownik),
/// nigdy hasła w postaci jawnej. Rola steruje kontekstem uprawnień po zalogowaniu.
/// </summary>
public sealed class User : Entity<Guid>
{
    private const int MinPasswordLength = 8;

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private User() { }
    private User(Guid id, string login, string displayName, string passwordHash, string passwordSalt, UserRole role)
    {
        Id = id;
        Login = login;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        Role = role;
        IsActive = true;
    }

    /// <summary>Login (bez rozróżniania wielkości liter — normalizowany do małych liter).</summary>
    public string Login { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Hash hasła w formacie PBKDF2 (base64).</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Sól losowa dla hasła (base64) — zapobiega atakom rainbow table.</summary>
    public string PasswordSalt { get; private set; }

    /// <summary>Rola użytkownika — wpływa na uprawnienia kontekstowe.</summary>
    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Data ostatniego udanego logowania.</summary>
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    /// <summary>Numer PWZ / identyfikator zawodowy — pole wymagane przez formalny wymóg identyfikacji.</summary>
    public string? ProfessionalLicenseNumber { get; private set; }

    public static User Create(
        Guid id,
        string login,
        string displayName,
        string plainTextPassword,
        UserRole role,
        string? professionalLicenseNumber = null)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator użytkownika jest wymagany.", nameof(id));
        }

        var normalizedLogin = login?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedLogin.Length < 3)
        {
            throw new ValidationException("Login musi mieć co najmniej 3 znaki.", nameof(login));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ValidationException("Imię i nazwisko są wymagane.", nameof(displayName));
        }

        var (hash, salt) = PasswordHasher.HashPassword(plainTextPassword);

        return new User(id, normalizedLogin, displayName.Trim(), hash, salt, role)
        {
            ProfessionalLicenseNumber = professionalLicenseNumber?.Trim()
        };
    }

    /// <summary>Weryfikacja hasła w formie jawnej (wymaga zabezpieczenia przed timing attack w serwisie logowania).</summary>
    public bool VerifyPassword(string plainTextPassword) =>
        PasswordHasher.VerifyPassword(plainTextPassword, PasswordHash, PasswordSalt);

    /// <summary>Rejestracja udanego logowania (audytowalne).</summary>
    public void RegisterLogin(DateTimeOffset timestampUtc)
    {
        LastLoginAtUtc = timestampUtc;
    }

    /// <summary>Zmiana hasła przez użytkownika — natychmiastowa, bez sesji starego hasła.</summary>
    public void ChangePassword(string newPlainTextPassword)
    {
        var (hash, salt) = PasswordHasher.HashPassword(newPlainTextPassword);
        PasswordHash = hash;
        PasswordSalt = salt;
    }

    /// <summary>Reguła BR-12: koordynator widzi wszystkie strefy, lekarz/pielęgniarka tylko własną.</summary>
    public bool CanManageAllZones() => Role == UserRole.Coordinator;

    /// <summary>Reguła BR-13: koordynator może zlecić rotację innym pracownikom.</summary>
    public bool CanOrderRotationOf(User target) =>
        Role == UserRole.Coordinator && !target.Id.Equals(Id);

    public void Deactivate()
    {
        IsActive = false;
    }

    public override string ToString() => $"{DisplayName} ({Login}, {Role})";
}
