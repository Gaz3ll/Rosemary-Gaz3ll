namespace SOR.Domain.Common;

/// <summary>
/// Wyjątek domenowy sygnalizujący konflikt współbieżnej modyfikacji tego samego pacjenta
/// ( optimistic concurrency / blokada logiczna karty pacjenta).
/// </summary>
public sealed class ConcurrentPatientModificationException : DomainException
{
    public ConcurrentPatientModificationException(Guid patientId, string holderLogin)
        : base("SOR-DOM-021", $"Karta pacjenta {patientId} jest obecnie modyfikowana przez użytkownika '{holderLogin}'.")
    {
        PatientId = patientId;
        HeldBy = holderLogin;
    }

    public Guid PatientId { get; }

    public string HeldBy { get; }
}

/// <summary>Błąd naruszenia wymogu kompletności danych przed wypisem pacjenta z SOR (BR-11).</summary>
public sealed class PatientDischargeBlockedException : DomainException
{
    public PatientDischargeBlockedException(params string[] reasons)
        : base("SOR-DOM-023", "Nie można wypisać pacjenta z SOR: " + string.Join("; ", reasons))
    {
        Reasons = reasons;
    }

    public IReadOnlyList<string> Reasons { get; }
}