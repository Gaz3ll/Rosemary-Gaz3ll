using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Presentation.Converters;
using SOR.Presentation.Mvvm;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// ViewModel okna „Historia pobytu” otwieranego dwukrotnym kliknięciem wiersza pacjenta.
/// Pokazuje pełny przebieg pobytu w SOR: dane przyjęcia, kolejne oceny Triage, przeniesienia
/// między strefami, zlecenia (procedury i badania) wraz ze stanem realizacji, podane leki
/// oraz wypisy. Widok jest wyłącznie do odczytu — nie zawiera operacji zmieniających kartę.
/// </summary>
public sealed class PatientHistoryViewModel : ObservableObject
{
    private readonly IPatientService _patientService;
    private readonly IClock _clock;

    private PatientDetailsDto? _details;

    public PatientHistoryViewModel(IPatientService patientService, IClock clock)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Pełne dane karty pacjenta; źródło wszystkich sekcji historii.</summary>
    public PatientDetailsDto? Details
    {
        get => _details;
        private set
        {
            if (!SetProperty(ref _details, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Age));
            OnPropertyChanged(nameof(AdmissionDisplay));
            OnPropertyChanged(nameof(ZoneDisplay));
            OnPropertyChanged(nameof(DiagnosisDisplay));
            OnPropertyChanged(nameof(ComplaintDisplay));
            OnPropertyChanged(nameof(DischargeDisplay));
            OnPropertyChanged(nameof(TriageSectionHeader));
            OnPropertyChanged(nameof(TransfersSectionHeader));
            OnPropertyChanged(nameof(OrdersSectionHeader));
            OnPropertyChanged(nameof(AdministrationsSectionHeader));
            OnPropertyChanged(nameof(DischargesSectionHeader));
            OnPropertyChanged(nameof(HasDischargeBlockers));
        }
    }

    /// <summary>Tytuł okna wraz z nazwiskiem pacjenta.</summary>
    public string Title => _details is null
        ? "Historia pobytu"
        : $"Historia pobytu — {_details.FullName}";

    /// <summary>Wiek pacjenta w latach, liczony od daty urodzenia do chwili otwarcia historii.</summary>
    public int Age => _details is null ? 0 : CalculateAge(_details.DateOfBirth);

    /// <summary>Data i godzina przyjęcia do SOR w strefie lokalnej.</summary>
    public string AdmissionDisplay => _details is null
        ? "—"
        : _details.RegisteredAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    /// <summary>Nazwa strefy pobytu albo informacja o braku przydziału.</summary>
    public string ZoneDisplay => string.IsNullOrWhiteSpace(_details?.ZoneName)
        ? "brak przydziału do strefy"
        : _details!.ZoneName;

    /// <summary>Rozpoznanie ICD-10 albo informacja o jego braku.</summary>
    public string DiagnosisDisplay => string.IsNullOrWhiteSpace(_details?.Diagnosis)
        ? "brak rozpoznania"
        : _details!.Diagnosis;

    /// <summary>Dolegliwości zgłoszone przy rejestracji; „—” gdy nie odnotowano.</summary>
    public string ComplaintDisplay => string.IsNullOrWhiteSpace(_details?.Complaint)
        ? "—"
        : _details!.Complaint;

    /// <summary>Sposób zakończenia pobytu albo informacja, że pacjent nadal przebywa w SOR.</summary>
    public string DischargeDisplay => _details?.Discharges.Count switch
    {
        null or 0 => "pobyt w toku — pacjent nadal w SOR",
        _ => $"{PolishEnumConverter.ToPolish(_details.Discharges[0].Type)} ({_details.Discharges[0].DischargedAtDisplay})",
    };

    /// <summary>Nagłówek sekcji ocen Triage z liczbą wpisów.</summary>
    public string TriageSectionHeader => $"Oceny Triage ({_details?.TriageHistory.Count ?? 0})";

    /// <summary>Nagłówek sekcji przeniesień między strefami z liczbą wpisów.</summary>
    public string TransfersSectionHeader => $"Przeniesienia między strefami ({_details?.Transfers.Count ?? 0})";

    /// <summary>Nagłówek sekcji zleceń (procedury i badania) z liczbą wpisów.</summary>
    public string OrdersSectionHeader => $"Procedury i badania ({_details?.Orders.Count ?? 0})";

    /// <summary>Nagłówek sekcji podanych leków z liczbą wpisów.</summary>
    public string AdministrationsSectionHeader => $"Podane leki ({_details?.Administrations.Count ?? 0})";

    /// <summary>Nagłówek sekcji wypisów z SOR z liczbą wpisów.</summary>
    public string DischargesSectionHeader => $"Wypisy z SOR ({_details?.Discharges.Count ?? 0})";

    /// <summary>Czy karta ma warunki blokujące wypis — decyduje o pokazaniu listy blokad.</summary>
    public bool HasDischargeBlockers => _details?.DischargeBlockers.Count > 0;

    /// <summary>Wczytuje pełną historię pobytu wskazanego pacjenta.</summary>
    public async Task LoadAsync(Guid patientId)
    {
        var details = await _patientService.GetPatientAsync(patientId).ConfigureAwait(true);
        Details = details;
    }

    private int CalculateAge(DateOnly dateOfBirth)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.LocalDateTime);
        var age = today.Year - dateOfBirth.Year;

        // Pacjent przed urodzinami w bieżącym roku jest o rok młodszy.
        if (dateOfBirth > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? 0 : age;
    }
}
