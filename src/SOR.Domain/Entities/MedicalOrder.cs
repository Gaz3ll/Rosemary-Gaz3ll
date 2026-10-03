using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>Encja <c>MedicalOrder</c> — zlecenie lekarskie. Otwarte zlecenia blokują zamknięcie karty (BR-10).</summary>
public sealed class MedicalOrder : Entity<Guid>
{
    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private MedicalOrder() { }
    private MedicalOrder(
        Guid id,
        Guid patientId,
        Guid orderedByUserId,
        MedicalOrderType type,
        string description,
        DateTimeOffset orderedAtUtc,
        UserRole orderedByRole,
        bool isUrgent)
    {
        Id = id;
        PatientId = patientId;
        OrderedByUserId = orderedByUserId;
        Type = type;
        Description = description;
        OrderedAtUtc = orderedAtUtc;
        OrderedByRole = orderedByRole;
        IsUrgent = isUrgent;
        State = MedicalOrderState.Open;
    }

    public Guid PatientId { get; private set; }

    public Guid OrderedByUserId { get; private set; }

    public MedicalOrderType Type { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset OrderedAtUtc { get; private set; }

    /// <summary>Rola osoby zlecającej — pielęgniarka nie może zlecać rozpoznań (BR-13).</summary>
    public UserRole OrderedByRole { get; private set; }

    public bool IsUrgent { get; private set; }

    public MedicalOrderState State { get; private set; }

    /// <summary>Czas realizacji zlecenia.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Data anulowania zlecenia.</summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Czy zlecenie blokuje zamknięcie karty pacjenta.</summary>
    public bool IsBlocking => State is MedicalOrderState.Open or MedicalOrderState.InProgress;

    public static MedicalOrder Create(
        Guid id,
        Guid patientId,
        Guid orderedByUserId,
        UserRole orderedByRole,
        MedicalOrderType type,
        string description,
        DateTimeOffset orderedAtUtc,
        bool isUrgent = false)
    {
        if (id == Guid.Empty || patientId == Guid.Empty || orderedByUserId == Guid.Empty)
        {
            throw new ValidationException("Identyfikatory zlecenia, pacjenta i zlecającego są wymagane.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 3)
        {
            throw new ValidationException("Opis zlecenia jest wymagany (min. 3 znaki).", nameof(description));
        }

        // BR-13: pielęgniarka nie wystawia zleceń rozpoznawczych / terapeutycznych wymagających recepty.
        if (orderedByRole == UserRole.Nurse && type == MedicalOrderType.Medication)
        {
            throw new ValidationException(
                "Pielęgniarka / ratownik nie może wystawić zlecenia podania leku (BR-13).", nameof(orderedByRole));
        }

        return new MedicalOrder(
            id,
            patientId,
            orderedByUserId,
            type,
            description.Trim(),
            orderedAtUtc,
            orderedByRole,
            isUrgent);
    }

    /// <summary>Zmiana statusu zlecenia — poprawność maszyny stanów egzekwowana w encji.</summary>
    public void ChangeState(MedicalOrderState newState, DateTimeOffset nowUtc, string? reason = null)
    {
        ValidateTransition(newState);

        State = newState;

        switch (newState)
        {
            case MedicalOrderState.Completed:
                CompletedAtUtc = nowUtc;
                break;
            case MedicalOrderState.Cancelled:
                CancelledAtUtc = nowUtc;
                CancellationReason = reason;
                break;
            default:
                break;
        }
    }

    /// <summary>Reguła BR-13: zlecenie lekarskie może oznaczyć jako zrealizowane wyłącznie lekarz lub koordynator.</summary>
    public void EnsureCanBeCompletedBy(UserRole role)
    {
        if (role is UserRole.Physician or UserRole.Coordinator)
        {
            return;
        }

        // Ratownik medyczny realizuje zlecenia, których wykonanie należy do jego zakresu
        // czynności: podanie leku oraz badanie obrazowe. Pozostałe typy zleceń pozostają
        // w gestii lekarza lub koordynatora.
        if (role is UserRole.Paramedic)
        {
            if (Type is not (MedicalOrderType.Medication or MedicalOrderType.Imaging))
            {
                throw new ValidationException(
                    "Ratownik medyczny może oznaczyć jako zrealizowane wyłącznie zlecenie podania leku " +
                    "lub badania obrazowego (BR-13).",
                    nameof(role));
            }

            return;
        }

        throw new ValidationException(
            "Oznaczenie zlecenia jako zrealizowanego jest możliwe wyłącznie przez lekarza lub koordynatora (BR-13).",
            nameof(role));
    }

    /// <summary>Czy zlecenie pozostaje do realizowania i może zostać oznaczone jako wykonane.</summary>
    public bool CanBeCompleted => State is MedicalOrderState.Open or MedicalOrderState.InProgress;

    private void ValidateTransition(MedicalOrderState newState)
    {
        var allowed = (State, newState) switch
        {
            (MedicalOrderState.Open, MedicalOrderState.Open) => true,
            (MedicalOrderState.Open, MedicalOrderState.InProgress) => true,
            (MedicalOrderState.Open, MedicalOrderState.Cancelled) => true,
            (MedicalOrderState.Open, MedicalOrderState.Completed) => true,
            (MedicalOrderState.InProgress, MedicalOrderState.Completed) => true,
            (MedicalOrderState.InProgress, MedicalOrderState.Cancelled) => true,
            (MedicalOrderState.Completed, _) => false,
            (MedicalOrderState.Cancelled, _) => false,
            _ => false
        };

        if (!allowed)
        {
            throw new ValidationException(
                $"Niedozwolona zmiana statusu zlecenia: {State} → {newState}.", nameof(newState));
        }
    }

    public override string ToString() => $"{Type}: {Description} [{State}]";
}
