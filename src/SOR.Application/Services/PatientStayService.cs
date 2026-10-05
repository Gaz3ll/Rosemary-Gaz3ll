using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Application.Services;

/// <summary>
/// Serwis listy pobytów — źródło danych dla widoku „Lista pobytów” (specyfikacja interfejsu, sekcja D).
/// Zajmuje się wyłącznie odczytem i projekcją: filtrowanie według zakresu dat i statusu oraz
/// wyprowadzenie kolumn widokowych (czas pobytu, numer dokumentacji, kategoria pilności).
/// </summary>
public sealed class PatientStayService : IPatientStayService
{
    /// <summary>Statusy pacjenta trwającego pobyt w SOR.</summary>
    private static readonly PatientState[] ActiveStates =
    [
        PatientState.Registered,
        PatientState.Triaged,
        PatientState.InTreatment,
        PatientState.AwaitingTransport,
    ];

    private readonly IUnitOfWork _unitOfWork;

    public PatientStayService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatientStayDto>> GetStaysAsync(
        StayQuery query,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var patients = await _unitOfWork.Patients
            .GetStaysAsync(query.FromUtc, query.ToUtc, cancellationToken)
            .ConfigureAwait(false);

        var zoneNames = await GetZoneNamesAsync(patients, cancellationToken).ConfigureAwait(false);
        var visitNumbers = BuildVisitNumbers(patients);

        var rows = patients
            .Where(patient => Matches(patient, query.Filter))
            .Select(patient => ToDto(patient, zoneNames, visitNumbers, nowUtc))
            .ToList();

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImagingStudyDto>> GetImagingStudiesAsync(
        StayQuery query,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stays = await GetStaysAsync(query, nowUtc, cancellationToken).ConfigureAwait(false);

        var patients = await _unitOfWork.Patients
            .GetStaysAsync(query.FromUtc, query.ToUtc, cancellationToken)
            .ConfigureAwait(false);

        var byId = patients.ToDictionary(patient => patient.Id);
        var studies = new List<ImagingStudyDto>();

        foreach (var stay in stays)
        {
            if (!byId.TryGetValue(stay.PatientId, out var patient))
            {
                continue;
            }

            foreach (var order in patient.Orders.Where(order =>
                         order.Type is MedicalOrderType.Imaging or MedicalOrderType.Consultation))
            {
                studies.Add(new ImagingStudyDto(
                    stay.PatientId,
                    stay.PatientName,
                    stay.Pesel,
                    order.Type,
                    order.Description,
                    order.State,
                    order.IsUrgent,
                    order.OrderedAtUtc,
                    order.CompletedAtUtc));
            }
        }

        return studies
            .OrderByDescending(study => study.OrderedAtUtc)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetZoneNamesAsync(
        IReadOnlyList<Patient> patients,
        CancellationToken cancellationToken)
    {
        var zoneIds = patients
            .Select(patient => patient.ZoneId)
            .Where(zoneId => zoneId is not null)
            .Select(zoneId => zoneId!.Value)
            .Distinct()
            .ToList();

        if (zoneIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var zones = await _unitOfWork.Zones
            .GetByIdsAsync(zoneIds, cancellationToken)
            .ConfigureAwait(false);

        return zones.ToDictionary(zone => zone.Id, zone => zone.Name);
    }

    /// <summary>Numeruje pobyty w obrębie jednego pacjenta (kolejne wizyty w SOR).</summary>
    private static Dictionary<Guid, int> BuildVisitNumbers(IReadOnlyList<Patient> patients)
    {
        var counters = new Dictionary<Guid, int>();

        foreach (var patient in patients.OrderBy(patient => patient.RegisteredAtUtc))
        {
            counters.TryGetValue(patient.Id, out var count);
            counters[patient.Id] = count + 1;
        }

        // Wiersze są posortowane malejąco po dacie przyjęcia, więc numerację budujemy
        // w kolejności rosnącej i przypisujemy pacjentowi najnowszy numer pobytu.
        return patients
            .GroupBy(patient => patient.Id)
            .ToDictionary(
                group => group.Key,
                group => counters[group.Key]);
    }

    private static bool Matches(Patient patient, StayFilter filter) => filter switch
    {
        StayFilter.All => true,
        StayFilter.CurrentlyInHospital => ActiveStates.Contains(patient.State),
        StayFilter.TransferredToDepartment => patient.State == PatientState.TransferredOut,
        StayFilter.Cancelled => patient.Discharges.Any(discharge =>
            discharge.Type == DischargeType.AtPatientRequest),
        StayFilter.CurrentlyInBay => patient.State == PatientState.Triaged,
        _ => false,
    };

    private static PatientStayDto ToDto(
        Patient patient,
        IReadOnlyDictionary<Guid, string> zoneNames,
        IReadOnlyDictionary<Guid, int> visitNumbers,
        DateTimeOffset nowUtc)
    {
        var discharge = patient.Discharges
            .OrderByDescending(item => item.DischargedAtUtc)
            .FirstOrDefault();

        var department = discharge?.DepartmentName
            ?? (patient.ZoneId is not null ? zoneNames.GetValueOrDefault(patient.ZoneId.Value) : null)
            ?? "—";

        var triage = patient.CurrentTriage?.Category ?? TriageCategory.Blue;
        var waitingSince = patient.ZoneAssignedAtUtc ?? patient.RegisteredAtUtc;

        return new PatientStayDto(
            Covid19: false,
            patient.Id,
            BuildPatientNumber(patient),
            patient.FirstName,
            patient.LastName,
            patient.Pesel,
            CalculateAge(patient.DateOfBirth, nowUtc),
            triage,
            patient.Complaint,
            nowUtc - waitingSince,
            patient.RegisteredAtUtc,
            discharge?.DischargedAtUtc,
            visitNumbers.GetValueOrDefault(patient.Id, 1),
            BuildMedicalRecordNumber(patient),
            department,
            patient.State,
            discharge?.Type,
            MainLedgerBook: "—",
            HasInsurance: null,
            IsOncological: false,
            ConsentGiven: null,
            BuildPriorityCategory(triage),
            IdhNumber: "—");
    }

    /// <summary>Numer dokumentacji medycznej w formacie MRN-XXXXXXXX (odpowiednik kolumny TOPSOR).</summary>
    private static string BuildMedicalRecordNumber(Patient patient) =>
        $"MRN-{patient.Id.ToString("N")[..8].ToUpperInvariant()}";

    /// <summary>Krótki numer pacjenta (segment „ID pacjenta” kolumny złożonej Pacjent).</summary>
    private static string BuildPatientNumber(Patient patient) =>
        $"PAC-{patient.Id.ToString("N")[..6].ToUpperInvariant()}";

    /// <summary>Wiek pacjenta w latach liczony na dzień odwołania.</summary>
    private static int CalculateAge(DateOnly dateOfBirth, DateTimeOffset nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc.LocalDateTime);
        var age = today.Year - dateOfBirth.Year;

        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? 0 : age;
    }

    /// <summary>Kategoria pilności wyprowadzona z oceny Triage (kolumna „Kat.”).</summary>
    private static string BuildPriorityCategory(TriageCategory category) => category switch
    {
        TriageCategory.Red => "1 — natychmiast",
        TriageCategory.Orange => "2 — pilne",
        TriageCategory.Yellow => "3 — pilne, ale stabilne",
        TriageCategory.Green => "4 — zwykłe",
        TriageCategory.Blue => "5 — brak pilności",
        _ => "—",
    };
}

/// <summary>Wiersz listy badań obrazowych (zakładka „Badania obrazowe”).</summary>
/// <param name="PatientId">Identyfikator pacjenta.</param>
/// <param name="PatientName">Nazwa pacjenta.</param>
/// <param name="Pesel">Numer PESEL.</param>
/// <param name="Type">Typ zlecenia.</param>
/// <param name="Description">Opis zlecenia.</param>
/// <param name="State">Stan zlecenia.</param>
/// <param name="IsUrgent">Zlecenie pilne.</param>
/// <param name="OrderedAtUtc">Data wystawienia zlecenia.</param>
/// <param name="CompletedAtUtc">Data realizacji zlecenia.</param>
