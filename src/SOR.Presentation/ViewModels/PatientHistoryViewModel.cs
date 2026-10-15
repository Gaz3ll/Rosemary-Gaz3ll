using System.Collections.ObjectModel;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Presentation.Converters;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// ViewModel okna „Historia pobytu” otwieranego dwukrotnym kliknięciem wiersza pacjenta.
/// Pokazuje pełny przebieg pobytu w SOR: dane przyjęcia, kolejne oceny Triage, przeniesienia
/// między strefami, zlecenia (procedury i badania) wraz ze stanem realizacji, podane leki
/// oraz wypisy. Okno jest jedynym miejscem, w którym zleca się procedury, badania, konsultacje
/// i leki, stosuje pakiety medyczne, wybiera rozpoznanie ICD-10 z katalogu (z wyszukiwaniem
/// po kodzie i nazwie) oraz rejestruje wypis pacjenta z SOR.
/// </summary>
public sealed class PatientHistoryViewModel : ObservableObject
{
    private readonly IPatientService _patientService;
    private readonly IClock _clock;
    private readonly IDepartmentCatalogService _departmentCatalog;
    private readonly IMedicalBundleService _bundleService;
    private readonly IIcd10CatalogService _icd10Catalog;
    private readonly SessionViewModel _session;

    private PatientDetailsDto? _details;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _hasChanges;

    // Dane formularza nowego zlecenia.
    private MedicalOrderType _orderType = MedicalOrderType.Consultation;
    private string _orderDescription = string.Empty;
    private bool _orderUrgent;

    // Dane sekcji pakietów medycznych.
    private string _bundleTerm = string.Empty;
    private MedicalBundleDto? _selectedBundle;

    // Dane sekcji rozpoznania ICD-10 (wybór pozycji z katalogu).
    private string _icd10Term = string.Empty;
    private Icd10CatalogEntryDto? _selectedDiagnosis;
    private string _icd10Code = string.Empty;

    // Dane formularza wypisu.
    private DischargeType _dischargeType = DischargeType.TreatmentCompleted;
    private DepartmentDto? _selectedDepartment;
    private string _dischargeReason = string.Empty;

    public PatientHistoryViewModel(
        IPatientService patientService,
        IClock clock,
        IDepartmentCatalogService departmentCatalog,
        IMedicalBundleService bundleService,
        IIcd10CatalogService icd10Catalog,
        SessionViewModel session)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _departmentCatalog = departmentCatalog ?? throw new ArgumentNullException(nameof(departmentCatalog));
        _bundleService = bundleService ?? throw new ArgumentNullException(nameof(bundleService));
        _icd10Catalog = icd10Catalog ?? throw new ArgumentNullException(nameof(icd10Catalog));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        AddOrderCommand = new AsyncRelayCommand(AddOrderAsync, () => !_isBusy && CanIssueOrders);
        CompleteOrderCommand = new AsyncRelayCommand(
            CompleteOrderAsync,
            parameter => !_isBusy && CanCompleteOrder(parameter as MedicalOrderDto));
        ApplyBundleCommand = new AsyncRelayCommand(ApplyBundleAsync, () => !_isBusy && CanIssueOrders);
        SetDiagnosisCommand = new AsyncRelayCommand(SetDiagnosisAsync, () => !_isBusy && CanEnterDiagnosis);
        DischargeCommand = new AsyncRelayCommand(DischargeAsync, () => !_isBusy && CanDischargePatient);
        SearchBundlesCommand = new AsyncRelayCommand(SearchBundlesAsync);
        SearchDiagnosesCommand = new AsyncRelayCommand(SearchDiagnosesAsync);

        OrderTypes = Enum.GetValues<MedicalOrderType>();
        DischargeTypes = Enum.GetValues<DischargeType>();
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
            OnPropertyChanged(nameof(CanDischargePatient));
            DischargeCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Czy podczas pracy z oknem zmieniono kartę pacjenta (zlecenie, rozpoznanie, wypis).</summary>
    public bool HasChanges
    {
        get => _hasChanges;
        private set => SetProperty(ref _hasChanges, value);
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

    /// <summary>BR-13: zlecenie lekarskie wystawia wyłącznie lekarz lub koordynator.</summary>
    public bool CanIssueOrders => _session.CurrentUser?.CanIssueOrders == true;

    /// <summary>BR-13: rozpoznanie ICD-10 wprowadza wyłącznie lekarz lub koordynator.</summary>
    public bool CanEnterDiagnosis => _session.CurrentUser?.CanEnterDiagnosis == true;

    /// <summary>BR-11/BR-13: wypis pacjenta rejestruje wyłącznie lekarz lub koordynator.</summary>
    public bool CanDischargePatient =>
        _session.CurrentUser?.CanDischargePatient == true && _details is not null;

    /// <summary>Typy zleceń dostępne w formularzu (procedury, badania, konsultacje, leki, obserwacje).</summary>
    public IReadOnlyList<MedicalOrderType> OrderTypes { get; }

    /// <summary>Sposoby zakończenia pobytu dostępne w formularzu wypisu (BR-11).</summary>
    public IReadOnlyList<DischargeType> DischargeTypes { get; }

    /// <summary>Oddziały szpitalne dostępne jako cel przekazania pacjenta.</summary>
    public ObservableCollection<DepartmentDto> Departments { get; } = new();

    /// <summary>Pakiety medyczne dostępne w oddziale (wynik wyszukiwania lub pełna lista).</summary>
    public ObservableCollection<MedicalBundleDto> Bundles { get; } = new();

    /// <summary>Rozpoznania ICD-10 (wynik wyszukiwania lub lista typowa dla SOR).</summary>
    public ObservableCollection<Icd10CatalogEntryDto> Diagnoses { get; } = new();

    public MedicalOrderType OrderType
    {
        get => _orderType;
        set => SetProperty(ref _orderType, value);
    }

    public string OrderDescription
    {
        get => _orderDescription;
        set => SetProperty(ref _orderDescription, value);
    }

    public bool OrderUrgent
    {
        get => _orderUrgent;
        set => SetProperty(ref _orderUrgent, value);
    }

    /// <summary>Fraza wyszukiwania pakietów (nazwa, kod lub wskazanie).</summary>
    public string BundleTerm
    {
        get => _bundleTerm;
        set => SetProperty(ref _bundleTerm, value);
    }

    /// <summary>Pakiet medyczny wskazany do zastosowania na karcie pacjenta.</summary>
    public MedicalBundleDto? SelectedBundle
    {
        get => _selectedBundle;
        set => SetProperty(ref _selectedBundle, value);
    }

    /// <summary>Fraza wyszukiwania rozpoznań ICD-10 (fragment kodu albo opisu).</summary>
    public string Icd10Term
    {
        get => _icd10Term;
        set => SetProperty(ref _icd10Term, value);
    }

    /// <summary>Pozycja katalogu wybrana z listy; jej kod trafia do pola rozpoznania.</summary>
    public Icd10CatalogEntryDto? SelectedDiagnosis
    {
        get => _selectedDiagnosis;
        set
        {
            if (SetProperty(ref _selectedDiagnosis, value) && value is not null)
            {
                Icd10Code = value.Code;
            }
        }
    }

    /// <summary>Kod rozpoznania wybrany z katalogu ICD-10.</summary>
    public string Icd10Code
    {
        get => _icd10Code;
        set => SetProperty(ref _icd10Code, value);
    }

    /// <summary>Sposób zakończenia pobytu wybrany w formularzu wypisu (BR-11).</summary>
    public DischargeType DischargeType
    {
        get => _dischargeType;
        set
        {
            if (SetProperty(ref _dischargeType, value))
            {
                OnPropertyChanged(nameof(IsDepartmentRequired));
                OnPropertyChanged(nameof(IsReasonRequired));
            }
        }
    }

    /// <summary>Oddział przyjmujący — pole obowiązkowe wyłącznie przy przekazaniu pacjenta.</summary>
    public DepartmentDto? SelectedDepartment
    {
        get => _selectedDepartment;
        set => SetProperty(ref _selectedDepartment, value);
    }

    /// <summary>Uzasadnienie wypisu — wymagane przy wypisie na własne żądanie i przy przekazaniu.</summary>
    public string DischargeReason
    {
        get => _dischargeReason;
        set => SetProperty(ref _dischargeReason, value);
    }

    /// <summary>Czy dla wybranego scenariusza trzeba wskazać oddział przyjmujący.</summary>
    public bool IsDepartmentRequired => _dischargeType == DischargeType.TransferToDepartment;

    /// <summary>Czy dla wybranego scenariusza trzeba podać uzasadnienie.</summary>
    public bool IsReasonRequired => _dischargeType != DischargeType.TreatmentCompleted;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            AddOrderCommand.RaiseCanExecuteChanged();
            CompleteOrderCommand.RaiseCanExecuteChanged();
            ApplyBundleCommand.RaiseCanExecuteChanged();
            SetDiagnosisCommand.RaiseCanExecuteChanged();
            DischargeCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Dodaje zlecenie lekarskie (procedura, badanie, konsultacja, lek, obserwacja).</summary>
    public AsyncRelayCommand AddOrderCommand { get; }

    /// <summary>Oznacza wskazane zlecenie jako zrealizowane (BR-13).</summary>
    public AsyncRelayCommand CompleteOrderCommand { get; }

    /// <summary>Zastosowuje wybrany pakiet medyczny na karcie pacjenta (BR-13).</summary>
    public AsyncRelayCommand ApplyBundleCommand { get; }

    /// <summary>Ustawia rozpoznanie ICD-10 (tylko lekarz lub koordynator).</summary>
    public AsyncRelayCommand SetDiagnosisCommand { get; }

    /// <summary>Rejestruje wypis pacjenta z SOR (BR-11).</summary>
    public AsyncRelayCommand DischargeCommand { get; }

    /// <summary>Wyszukuje pakiety medyczne po nazwie, kodzie lub wskazaniu.</summary>
    public AsyncRelayCommand SearchBundlesCommand { get; }

    /// <summary>Wyszukuje rozpoznania ICD-10 po kodzie lub opisie.</summary>
    public AsyncRelayCommand SearchDiagnosesCommand { get; }

    /// <summary>Wczytuje pełną historię pobytu pacjenta oraz dane formularzy akcji.</summary>
    public async Task LoadAsync(Guid patientId)
    {
        var details = await _patientService.GetPatientAsync(patientId).ConfigureAwait(true);
        Details = details;

        await LoadDepartmentsAsync().ConfigureAwait(true);
        await LoadBundlesAsync().ConfigureAwait(true);
        await LoadDiagnosesAsync().ConfigureAwait(true);
    }

    private async Task LoadBundlesAsync()
    {
        try
        {
            var bundles = string.IsNullOrWhiteSpace(_bundleTerm)
                ? await _bundleService.GetAllAsync(CancellationToken.None).ConfigureAwait(true)
                : await _bundleService.SearchAsync(_bundleTerm, CancellationToken.None).ConfigureAwait(true);

            Replace(Bundles, bundles);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task LoadDiagnosesAsync()
    {
        try
        {
            var entries = string.IsNullOrWhiteSpace(_icd10Term)
                ? await _icd10Catalog.GetEmergencyRelevantAsync(CancellationToken.None).ConfigureAwait(true)
                : await _icd10Catalog.SearchAsync(_icd10Term, CancellationToken.None).ConfigureAwait(true);

            Replace(Diagnoses, entries);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task LoadDepartmentsAsync()
    {
        try
        {
            var departments = await _departmentCatalog.GetAllAsync().ConfigureAwait(true);

            Departments.Clear();

            foreach (var department in departments)
            {
                Departments.Add(department);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task SearchBundlesAsync()
    {
        await LoadBundlesAsync().ConfigureAwait(true);
        StatusMessage = $"Dostępne pakiety medyczne: {Bundles.Count}.";
    }

    private async Task ApplyBundleAsync()
    {
        if (_details is null || _isBusy)
        {
            return;
        }

        if (_selectedBundle is null)
        {
            StatusMessage = "Wybierz pakiet medyczny.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _bundleService
                .ApplyAsync(_details.Id, _selectedBundle.Id, itemIds: null, CancellationToken.None)
                .ConfigureAwait(true);

            Details = patient;
            HasChanges = true;
            StatusMessage = $"Zastosowano pakiet „{_selectedBundle.Name}” ({_selectedBundle.OrderCount} zleceń).";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchDiagnosesAsync()
    {
        await LoadDiagnosesAsync().ConfigureAwait(true);
        StatusMessage = string.IsNullOrWhiteSpace(_icd10Term)
            ? $"Rozpoznania typowe dla SOR: {Diagnoses.Count}."
            : $"Znaleziono rozpoznań: {Diagnoses.Count}.";
    }

    private async Task AddOrderAsync()
    {
        if (_details is null || _isBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_orderDescription))
        {
            StatusMessage = "Opis zlecenia jest wymagany.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _patientService.AddOrderAsync(
                _details.Id,
                _orderType,
                _orderDescription.Trim(),
                _orderUrgent,
                CancellationToken.None).ConfigureAwait(true);

            Details = patient;
            HasChanges = true;

            OrderDescription = string.Empty;
            OrderUrgent = false;
            StatusMessage = "Zapisano zlecenie.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// BR-13: zlecenie może oznaczyć jako zrealizowane lekarz lub koordynator, a ratownik
    /// medyczny dodatkowo zlecenie podania leku oraz badania obrazowego. Zlecenia innych
    /// typów (konsultacje, zabiegi, obserwacje) pozostają w gestii lekarza.
    /// </summary>
    private bool CanCompleteOrder(MedicalOrderDto? order)
    {
        var user = _session.CurrentUser;

        if (user is null || order is null || !user.CanExecuteOrders)
        {
            return false;
        }

        var isOpen = order.State is MedicalOrderState.Open or MedicalOrderState.InProgress;

        if (!isOpen)
        {
            return false;
        }

        return user.Role is not UserRole.Paramedic ||
               order.Type is MedicalOrderType.Medication or MedicalOrderType.Imaging;
    }

    private async Task CompleteOrderAsync(object? parameter)
    {
        if (parameter is not MedicalOrderDto order || _details is null || _isBusy)
        {
            return;
        }

        if (!_details.Orders.Any(item => item.Id == order.Id))
        {
            StatusMessage = "Zlecenie dotyczy innej karty pacjenta.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _patientService.ChangeOrderStateAsync(
                order.Id,
                MedicalOrderState.Completed,
                reason: null,
                CancellationToken.None).ConfigureAwait(true);

            Details = patient;
            HasChanges = true;
            StatusMessage = $"Zlecenie „{order.Description}” oznaczone jako zrealizowane.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetDiagnosisAsync()
    {
        if (_details is null || _isBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_icd10Code))
        {
            StatusMessage = "Wybierz rozpoznanie z katalogu ICD-10.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _icd10Catalog
                .AssignAsync(_details.Id, _icd10Code.Trim(), CancellationToken.None)
                .ConfigureAwait(true);

            Details = patient;
            HasChanges = true;

            SelectedDiagnosis = null;
            Icd10Code = string.Empty;
            StatusMessage = "Zapisano rozpoznanie ICD-10.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DischargeAsync()
    {
        if (_details is null || _isBusy)
        {
            return;
        }

        var type = _dischargeType;
        var departmentId = IsDepartmentRequired ? _selectedDepartment?.Id : null;
        var reason = string.IsNullOrWhiteSpace(_dischargeReason) ? null : _dischargeReason.Trim();

        IsBusy = true;

        try
        {
            var patient = await _patientService.DischargePatientAsync(
                _details.Id,
                type,
                departmentId,
                reason,
                CancellationToken.None).ConfigureAwait(true);

            Details = patient;
            HasChanges = true;

            DischargeReason = string.Empty;
            StatusMessage = $"Wypisano pacjenta z SOR ({DescribeDischargeType(type)}).";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DescribeDischargeType(DischargeType type) => type switch
    {
        DischargeType.AtPatientRequest => "wypis na własne żądanie",
        DischargeType.TransferToDepartment => "przekazanie na inny oddział",
        _ => "zakończenie leczenia"
    };

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
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
