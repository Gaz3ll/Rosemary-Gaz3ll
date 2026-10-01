using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.Entities;

/// <summary>
/// Agregat <c>Patient</c> — karta pacjenta SOR. Hermetyzuje stan kliniczny, kod Triage,
/// przypisanie do strefy oraz blokadę współbieżnej modyfikacji (BR-20).
/// </summary>
public sealed class Patient : Entity<Guid>
{
    private readonly List<TriageAssessment> _triageHistory = new();
    private readonly List<MedicalOrder> _orders = new();
    private readonly List<ZoneTransfer> _transfers = new();

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private Patient() { }
    private Patient(
        Guid id,
        string pesel,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        PatientGender gender,
        string? complaint)
    {
        Id = id;
        Pesel = pesel;
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Complaint = complaint;
        State = PatientState.Registered;
        RegisteredAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>PESEL — klucz biznesowy karty pacjenta (BR-18).</summary>
    public string Pesel { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public DateOnly DateOfBirth { get; private set; }

    public PatientGender Gender { get; private set; }

    /// <summary>Zgłaszane dolegliwości / powód przyjęcia.</summary>
    public string? Complaint { get; private set; }

    /// <summary>Aktualny stan pacjenta w przepływie klinicznym.</summary>
    public PatientState State { get; private set; }

    /// <summary>Strefa, w której pacjent aktualnie przebywa (null = jeszcze nieprzydzielony).</summary>
    public Guid? ZoneId { get; private set; }

    /// <summary>Data rejestracji w module wstępnym SOR.</summary>
    public DateTimeOffset RegisteredAtUtc { get; private set; }

    /// <summary>Data przypisania do strefy — podstawa wyliczania czasu oczekiwania.</summary>
    public DateTimeOffset? ZoneAssignedAtUtc { get; private set; }

    /// <summary>Data zamknięcia karty.</summary>
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>Login użytkownika aktualnie blokującego kartę — realizuje BR-20.</summary>
    public string? LockedBy { get; private set; }

    public DateTimeOffset? LockedAtUtc { get; private set; }

    /// <summary>Rozpoznanie ICD-10 — warunek poprawności zamknięcia karty (BR-09).</summary>
    public Icd10Code? Diagnosis { get; private set; }

    /// <summary>Aktualna ocena Triage (najnowsza w historii).</summary>
    public TriageAssessment? CurrentTriage { get; private set; }

    public IReadOnlyCollection<TriageAssessment> TriageHistory => _triageHistory.AsReadOnly();

    public IReadOnlyCollection<MedicalOrder> Orders => _orders.AsReadOnly();

    public IReadOnlyCollection<ZoneTransfer> Transfers => _transfers.AsReadOnly();

    public string FullName => $"{LastName} {FirstName}";

    // ---------- Fabryka ----------

    /// <summary>Rejestracja pacjenta w module wstępnym SOR (BR-18).</summary>
    public static Patient Register(
        Guid id,
        string pesel,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        PatientGender gender,
        string? complaint,
        DateTimeOffset registeredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator pacjenta jest wymagany.", nameof(id));
        }

        ValidatePesel(pesel);

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new ValidationException("Imię i nazwisko pacjenta są wymagane (BR-18).", nameof(firstName));
        }

        var age = CalculateAge(dateOfBirth, registeredAtUtc);
        if (age is < 0 or > 130)
        {
            throw new ValidationException("Data urodzenia pacjenta jest nieprawidłowa.", nameof(dateOfBirth));
        }

        return new Patient(id, pesel.Trim(), firstName.Trim(), lastName.Trim(), dateOfBirth, gender, complaint?.Trim())
        {
            RegisteredAtUtc = registeredAtUtc
        };
    }

    // ---------- Triage ----------

    /// <summary>
    /// Wykonanie oceny Triage. Na podstawie kodu Triage i progu ważności system automatycznie
    /// proponuje strefę docelową (BR-01a: Czerwony → Część ratunkowa).
    /// </summary>
    public TriageAssessment AssignTriage(TriageAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        if (State is PatientState.Closed or PatientState.TransferredOut)
        {
            throw new ValidationException("Nie można wykonać Triage dla pacjenta zamkniętego lub wydanego.", nameof(assessment));
        }

        _triageHistory.Add(assessment);
        CurrentTriage = assessment;

        if (State == PatientState.Registered)
        {
            State = PatientState.Triaged;
        }

        return assessment;
    }

    /// <summary>Ponowna ocena Triage — dozwolona przy zmianie stanu klinicznego.</summary>
    public bool RequiresRetriage(DateTimeOffset nowUtc) =>
        CurrentTriage is not null &&
        !CurrentTriage.IsRetriageNeeded(nowUtc) &&
        nowUtc - CurrentTriage.AssessedAtUtc > TimeSpan.FromMinutes(15);

    // ---------- Przypisanie do strefy ----------

    /// <summary>Przypisanie pacjenta do strefy (wymaga wcześniejszego Triage — BR-01a).</summary>
    public void AssignToZone(Guid zoneId, DateTimeOffset assignedAtUtc)
    {
        if (zoneId == Guid.Empty)
        {
            throw new ValidationException("Strefa jest wymagana.", nameof(zoneId));
        }

        if (State is PatientState.Closed or PatientState.TransferredOut)
        {
            throw new ValidationException("Nie można przypisać strefy pacjentowi zamkniętemu.", nameof(zoneId));
        }

        if (CurrentTriage is null)
        {
            throw new ValidationException(
                "Pacjent musi zostać poddany segregacji medycznej przed przydziałem do strefy (BR-01a).",
                nameof(zoneId));
        }

        ZoneId = zoneId;
        ZoneAssignedAtUtc = assignedAtUtc;
        State = PatientState.InTreatment;
    }

    /// <summary>Wydanie pacjenta do transportu — stan poprzedzający zamknięcie karty (BR-11).</summary>
    public void MarkAwaitingTransport()
    {
        if (State is PatientState.Closed or PatientState.TransferredOut)
        {
            throw new ValidationException("Pacjent został już wydany z SOR.", nameof(State));
        }

        State = PatientState.AwaitingTransport;
    }

    // ---------- Zlecenia ----------

    public MedicalOrder AddOrder(MedicalOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (State is PatientState.Closed or PatientState.TransferredOut)
        {
            throw new ValidationException("Nie można dodać zlecenia do zamkniętej karty pacjenta.", nameof(order));
        }

        _orders.Add(order);
        return order;
    }

    public IEnumerable<MedicalOrder> OpenOrders =>
        _orders.Where(o => o.State is MedicalOrderState.Open or MedicalOrderState.InProgress);

    /// <summary>Reguła BR-10: pacjent bez otwartych zleceń nie może zostać wydany z SOR.</summary>
    public bool HasOpenOrders => OpenOrders.Any();

    // ---------- Rozpoznanie i zamknięcie ----------

    /// <summary>Ustawienie rozpoznania ICD-10 (wymagane przed zamknięciem karty).</summary>
    public void SetDiagnosis(Icd10Code diagnosis)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);
        Diagnosis = diagnosis;
    }

    /// <summary>
    /// Próba zamknięcia karty pacjenta. Waliduje reguły BR-09/BR-10/BR-11 i w razie naruszenia
    /// rzuca <see cref="PatientCardClosureBlockedException"/> z listą powodów.
    /// </summary>
    public void Close(DateTimeOffset closedAtUtc, bool transportCompleted)
    {
        var context = new PatientClosureContext(
            Id,
            Diagnosis is not null,
            OpenOrders.Count(),
            State,
            !transportCompleted);

        PatientCardClosurePolicy.EnsureCanClose(context);

        State = PatientState.Closed;
        ClosedAtUtc = closedAtUtc;
    }

    /// <summary>Bezpieczna wersja zamknięcia zwracająca listę powodów blokady zamiast wyjątku (dla UI).</summary>
    public IReadOnlyList<string> CheckClosureBlockers(bool transportCompleted)
    {
        var context = new PatientClosureContext(
            Id,
            Diagnosis is not null,
            OpenOrders.Count(),
            State,
            !transportCompleted);

        return PatientCardClosurePolicy.GetBlockingReasons(context);
    }

    // ---------- Blokada współbieżnej modyfikacji (BR-20) ----------

    /// <summary>
    /// Przejmuje blokadę logiczną karty. Wygasłe blokady (starsze niż timeout) są przejmowane automatycznie.
    /// </summary>
    public void AcquireLock(string login, DateTimeOffset nowUtc, TimeSpan timeout)
    {
        if (State is PatientState.Closed or PatientState.TransferredOut)
        {
            throw new ValidationException("Karta pacjenta jest zamknięta — brak możliwości edycji.", nameof(login));
        }

        if (LockedBy is not null && LockedBy != login && LockedAtUtc + timeout > nowUtc)
        {
            throw new ConcurrentPatientModificationException(Id, LockedBy);
        }

        LockedBy = login;
        LockedAtUtc = nowUtc;
    }

    /// <summary>Zwalnia blokadę karty.</summary>
    public void ReleaseLock(string login)
    {
        if (LockedBy is not null && LockedBy != login)
        {
            throw new ConcurrentPatientModificationException(Id, LockedBy);
        }

        LockedBy = null;
        LockedAtUtc = null;
    }

    // ---------- Walidacja ----------

    /// <summary>Walidacja numeru PESEL — suma kontrolna modulo 11 (BR-18).</summary>
    private static void ValidatePesel(string? pesel)
    {
        var value = pesel?.Trim() ?? string.Empty;

        if (value.Length != 11 || !value.All(char.IsDigit))
        {
            throw new ValidationException("PESEL musi składać się z 11 cyfr (BR-18).", nameof(pesel));
        }

        int[] weights = { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        var sum = weights.Select((w, i) => w * int.Parse(value[i].ToString())).Sum();
        var control = sum % 11;
        var expected = control == 10 ? 0 : control;

        if (expected != int.Parse(value[10].ToString()))
        {
            throw new ValidationException($"PESEL '{value}' nie przechodzi walidacji sumy kontrolnej (BR-18).", nameof(pesel));
        }
    }

    private static int CalculateAge(DateOnly dateOfBirth, DateTimeOffset nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    // ---------- Wywołania dla warstwy trwałości ----------

    /// <summary>Rejestruje przeniesienie pacjenta w historii podróży po strefach (BR-19).</summary>
    public void RecordTransfer(ZoneTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        _transfers.Add(transfer);
        RaiseDomainEvent(new PatientTransferredEvent
        {
            TransferId = transfer.Id,
            PatientId = Id,
            FromZoneId = transfer.FromZoneId,
            ToZoneId = transfer.ToZoneId
        });
    }

    internal void AttachTriage(TriageAssessment assessment)
    {
        _triageHistory.Add(assessment);
        if (CurrentTriage is null || assessment.AssessedAtUtc >= CurrentTriage.AssessedAtUtc)
        {
            CurrentTriage = assessment;
        }
    }

    internal void AttachOrder(MedicalOrder order) => _orders.Add(order);

    internal void AttachTransfer(ZoneTransfer transfer) => _transfers.Add(transfer);

    public override string ToString() => $"{FullName} ({Pesel}), stan: {State}, strefa: {ZoneId?.ToString() ?? "brak"}";
}
