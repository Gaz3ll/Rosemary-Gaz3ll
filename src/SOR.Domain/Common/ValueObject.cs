using System.Diagnostics.CodeAnalysis;

namespace SOR.Domain.Common;

/// <summary>
/// Wspólna baza obiektów wartości: brak tożsamości, pełna kontrola nad kompozycją (metoda <c>Create</c>),
/// walidacja w konstruktorze oraz porównanie wartościowe.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>Komponenty składowe porównywane rekurencyjnie (kompozyt).</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <summary>Waliduje komponenty po złożeniu — wywoływana z każdej fabryki <c>Create</c>.</summary>
    protected virtual void Validate()
    {
    }

    public bool Equals(ValueObject? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    public override string ToString() =>
        string.Join(" | ", GetEqualityComponents().Select(c => c?.ToString() ?? "null"));

    /// <summary>Zabezpiecza parametry wymagane w konstruktorach obiektów wartości.</summary>
    protected static string EnsureNotEmpty(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"Pole '{paramName}' nie może być puste.", paramName);
        }

        return value.Trim();
    }

    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Hermetyzacja domenowa.")]
    protected static T EnsureNotNull<T>(T? value, string paramName) where T : class
    {
        if (value is null)
        {
            throw new ValidationException($"Pole '{paramName}' jest wymagane.", paramName);
        }

        return value;
    }
}