using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>
/// Wiersz listy pobytów (widok „Karta pobytu"). Zestaw kolumn odpowiada specyfikacji
/// interfejsu: COVID-19, Pacjent, Triage, Objawy, Czas, Data przyjęcia, Data wypisu, Nr, TOPSOR,
/// Oddział, Księga główna, Ubezp., Onkologiczna, Zgoda, Kat., IDH.
/// </summary>
/// <param name="Covid19">Przyjęcie z powodu COVID-19 — brak takiego atrybutu w modelu domenowym.</param>
/// <param name="PatientId">Identyfikator pacjenta (GUID) — segment „ID pacjenta” kolumny Pacjent.</param>
/// <param name="PatientNumber">Krótki numer pacjenta wyświetlany w kolumnie Pacjent.</param>
/// <param name="FirstName">Imię pacjenta.</param>
/// <param name="LastName">Nazwisko pacjenta.</param>
/// <param name="Pesel">Numer PESEL.</param>
/// <param name="Age">Wiek pacjenta w latach.</param>
/// <param name="Triage">Ostatnia ocena Triage.</param>
/// <param name="Complaint">Objawy zgłoszone przy rejestracji.</param>
/// <param name="TimeInZone">Czas pobytu w strefie.</param>
/// <param name="AdmittedAtUtc">Data przyjęcia do SOR.</param>
/// <param name="DischargedAtUtc">Data wypisu (null, jeśli pacjent pozostaje w SOR).</param>
/// <param name="VisitNumber">Numer kolejnego pobytu pacjenta w SOR.</param>
/// <param name="MedicalRecordNumber">Numer dokumentacji medycznej (TOPSOR).</param>
/// <param name="Department">Strefa pobytu albo oddział przekazania.</param>
/// <param name="State">Stan karty pacjenta.</param>
/// <param name="DischargeType">Sposób zakończenia pobytu.</param>
/// <param name="MainLedgerBook">Księga główna — brak danych w modelu.</param>
/// <param name="HasInsurance">Ubezpieczenie — brak danych w modelu.</param>
/// <param name="IsOncological">Przyjęcie onkologiczne — brak danych w modelu.</param>
/// <param name="ConsentGiven">Zgoda — brak danych w modelu.</param>
/// <param name="PriorityCategory">Kategoria pilności wyprowadzona z Triage.</param>
/// <param name="IdhNumber">Numer IDH — brak danych w modelu.</param>
public sealed record PatientStayDto(
    bool Covid19,
    Guid PatientId,
    string PatientNumber,
    string FirstName,
    string LastName,
    string Pesel,
    int Age,
    TriageCategory Triage,
    string? Complaint,
    TimeSpan TimeInZone,
    DateTimeOffset AdmittedAtUtc,
    DateTimeOffset? DischargedAtUtc,
    int VisitNumber,
    string MedicalRecordNumber,
    string? Department,
    PatientState State,
    DischargeType? DischargeType,
    string MainLedgerBook,
    bool? HasInsurance,
    bool IsOncological,
    bool? ConsentGiven,
    string PriorityCategory,
    string IdhNumber)
{
    /// <summary>Nazwisko i imię pacjenta.</summary>
    public string PatientName => $"{LastName} {FirstName}";

    /// <summary>
    /// Kolumna złożona Pacjent: PESEL, ID pacjenta, imię, nazwisko oraz wiek w latach,
    /// np. „00210512345, PAC-0001, Jan, Kowalski, 26 lat".
    /// </summary>
    public string PatientDisplay =>
        $"{Pesel}, {PatientNumber}, {FirstName}, {LastName}, {Age} lat";
    /// <summary>Data wypisu w formacie skróconym; „—” dla pacjenta przebywającego w SOR.</summary>
    public string DischargedAtDisplay => DischargedAtUtc is null
        ? "—"
        : DischargedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    /// <summary>Data przyjęcia w formacie skróconym.</summary>
    public string AdmittedAtDisplay => AdmittedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    /// <summary>Czas pobytu w strefie w formacie godzinowym.</summary>
    public string TimeInZoneDisplay =>
        $"{(int)TimeInZone.TotalHours:D2}:{TimeInZone.Minutes:D2}:{TimeInZone.Seconds:D2}";
}

/// <summary>
/// Filtry listy pobytów. Filtry odpowiadają radio buttonom z panelu zakresu dat i statusów;
/// wartości bez odpowiednika w modelu domenowym zwracają pusty zbiór, aby nie udawać
/// danych, których system nie przechowuje.
/// </summary>
public enum StayFilter
{
    /// <summary>Wszyscy pacjenci z zakresu dat.</summary>
    All = 0,

    /// <summary>Aktualnie w szpitalu — pacjenci w trakcie pobytu w SOR.</summary>
    CurrentlyInHospital = 1,

    /// <summary>Przeniesieni na oddział — wypis typu przekazanie.</summary>
    TransferredToDepartment = 2,

    /// <summary>Anulowani — wypis na własne żądanie (zlecenia otwarte anulowane).</summary>
    Cancelled = 3,

    /// <summary>Odmowa — brak odpowiednika w modelu domenowym.</summary>
    Refused = 4,

    /// <summary>Aktualnie w izbie — pacjenci oczekujący w strefie segregacji (TRI).</summary>
    CurrentlyInBay = 5,

    /// <summary>COVID-19 — brak odpowiednika w modelu domenowym.</summary>
    Covid19 = 6,

    /// <summary>Brak ubezpieczenia — brak odpowiednika w modelu domenowym.</summary>
    NoInsurance = 7,

    /// <summary>Wypisani bez procedury roli admitowanej w izbie — brak odpowiednika w modelu.</summary>
    DischargedWithoutFormalities = 8,

    /// <summary>Księgowa – weryfikacja — brak odpowiednika w modelu domenowym.</summary>
    LedgerVerification = 9,
}

/// <summary>Zakres dat i dodatkowe warunki filtrowania listy pobytów.</summary>
/// <param name="FromUtc">Data od (włącznie).</param>
/// <param name="ToUtc">Data do (włącznie).</param>
/// <param name="Filter">Wybrany filtr statusu.</param>
/// <param name="WithoutRefusalCard">Brak karty odmowy — brak odpowiednika w modelu domenowym.</param>
public sealed record StayQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc, StayFilter Filter, bool WithoutRefusalCard);

/// <summary>Zlecenie badania obrazowego wyświetlane w zakładce „Badania obrazowe”.</summary>
public sealed record ImagingStudyDto(
    Guid PatientId,
    string PatientName,
    string Pesel,
    MedicalOrderType OrderType,
    string Description,
    MedicalOrderState State,
    bool IsUrgent,
    DateTimeOffset OrderedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    public string PatientDisplay => $"{PatientName} ({Pesel})";

    public string Status => State switch
    {
        MedicalOrderState.Open => "Nowe",
        MedicalOrderState.InProgress => "W realizacji",
        MedicalOrderState.Completed => "Zrealizowane",
        MedicalOrderState.Cancelled => "Anulowane",
        _ => "—"
    };

    public string OrderedAtDisplay => OrderedAtUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
}