namespace SOR.Domain.Common;

/// <summary>
/// Wyjątek bazowy dla wszystkich błędów domenowych systemu SOR.
/// Hermetyzuje kontekst powstania błędu i kod maszynowy (wymagany do testów i mapowania na komunikaty UI).
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    protected DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Stabilny kod błędu, np. <c>SOR-DOM-014</c>.</summary>
    public string Code { get; }
}

/// <summary>Błąd walidacji niezmiennika encji lub obiektu wartości.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message, params string[] brokenRules)
        : base("SOR-DOM-001", message)
    {
        BrokenRules = brokenRules;
    }

    /// <summary>Lista naruszonych reguł walidacyjnych (przydatna w testach jednostkowych).</summary>
    public IReadOnlyList<string> BrokenRules { get; }
}