using System.Security.Cryptography;
using System.Text;

namespace SOR.Domain.Entities;

/// <summary>
/// Usługa haszowania haseł (PBKDF2-HMAC-SHA256). Wydzielona z encji <see cref="User"/>,
/// aby haszowanie było testowalne i wymienne bez zmiany modelu domenowego.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    /// <summary>Tworzy parę (hash, sól) dla podanego hasła.</summary>
    public static (string Hash, string Salt) HashPassword(string plainTextPassword)
    {
        ValidatePasswordStrength(plainTextPassword);

        var saltBytes = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hashBytes = DeriveKey(plainTextPassword, saltBytes);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    /// <summary>Weryfikuje hasło w formie jawnej względem zapisanych danych.</summary>
    public static bool VerifyPassword(string plainTextPassword, string storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(plainTextPassword) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var expectedHash = Convert.FromBase64String(storedHash);
            var actualHash = DeriveKey(plainTextPassword, saltBytes);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(HashSizeBytes);
    }

    /// <summary>Polityka siły hasła: min. 8 znaków, cyfra oraz znak specjalny.</summary>
    private static void ValidatePasswordStrength(string plainTextPassword)
    {
        if (string.IsNullOrWhiteSpace(plainTextPassword) || plainTextPassword.Length < 8)
        {
            throw new Common.ValidationException("Hasło musi mieć co najmniej 8 znaków.", nameof(plainTextPassword));
        }

        if (!plainTextPassword.Any(char.IsDigit))
        {
            throw new Common.ValidationException("Hasło musi zawierać co najmniej jedną cyfrę.", nameof(plainTextPassword));
        }

        if (!plainTextPassword.Any(char.IsUpper) || !plainTextPassword.Any(char.IsLower))
        {
            throw new Common.ValidationException("Hasło musi zawierać wielkie i małe litery.", nameof(plainTextPassword));
        }
    }
}