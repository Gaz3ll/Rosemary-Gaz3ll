namespace SOR.Domain.Common;

/// <summary>
/// Wyjątek domenowy rzucany, gdy użytkownik próbuje zmienić strefę dyżuru w sposób niedozwolony
/// (ta sama strefa, brak uprawnień, brak uzasadnienia, przekroczenie limitu osób).
/// </summary>
public sealed class InvalidZoneReassignmentException : DomainException
{
    public InvalidZoneReassignmentException(string message)
        : base("SOR-DOM-014", message)
    {
    }

    public InvalidZoneReassignmentException(string code, string message)
        : base(code, message)
    {
    }
}