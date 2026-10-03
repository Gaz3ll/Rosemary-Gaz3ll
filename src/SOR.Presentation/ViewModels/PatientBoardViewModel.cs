using System.Collections.ObjectModel;
using System.Globalization;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// ViewModel pulpitu strefy: kolejka oczekujących na triage oraz lista pacjentów
/// przydzielonych do bieżącej strefy. Odpowiada za BR-03 (przyjęcie i triage),
/// BR-04 (walidacja zdolności przyjęciowej) oraz BR-06 (widoczność obciążenia).
/// </summary>
public sealed class PatientBoardViewModel : ObservableObject
{
    private readonly IPatientService _patientService;
    private readonly IZoneLoadMonitoringService _monitoringService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SessionViewModel _session;
    private readonly UiThreadDispatcher _dispatcher;

    private PatientCardDto? _selectedPatient;
    private PatientDetailsDto? _selectedPatientDetails;
    private ZoneLoadDto? _zoneLoad;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    // Dane formularza rejestracji i triage
    private string _pesel = string.Empty;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _dateOfBirth = string.Empty;
    private PatientGender _gender = PatientGender.Male;
    private string _peselDerivedHint = string.Empty;
    private string _complaint = string.Empty;
    private TriageCategory _triageCategory = TriageCategory.Blue;
    private string _clinicalJustification = string.Empty;
    private string _vitalSigns = string.Empty;
    private PatientCardDto? _selectedTriagePatient;
    private ZoneLoadDto? _selectedZoneForAssignment;
    private Guid? _zoneAssignmentPatientId;
    private string? _zoneAssignmentPatientName;
    private MedicalOrderType _orderType = MedicalOrderType.Consultation;
    private string _orderDescription = string.Empty;
    private bool _orderUrgent;
    private string _icd10Code = string.Empty;

    public PatientBoardViewModel(
        IPatientService patientService,
        IZoneLoadMonitoringService monitoringService,
        IUnitOfWork unitOfWork,
        SessionViewModel session,
        UiThreadDispatcher dispatcher)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RegisterPatientCommand = new AsyncRelayCommand(RegisterPatientAsync, () => !_isBusy);
        PerformTriageCommand = new AsyncRelayCommand(PerformTriageAsync, () => !_isBusy);
        AssignToZoneCommand = new AsyncRelayCommand(AssignToZoneAsync, () => !_isBusy);
        SelectPatientCommand = new AsyncRelayCommand(SelectPatientAsync);
        AddOrderCommand = new AsyncRelayCommand(AddOrderAsync, () => !_isBusy);
        CompleteOrderCommand = new AsyncRelayCommand(
            CompleteOrderAsync,
            parameter => CanCompleteOrder(parameter as MedicalOrderDto));
        SetDiagnosisCommand = new AsyncRelayCommand(SetDiagnosisAsync, () => !_isBusy);
        LockCardCommand = new AsyncRelayCommand(LockCardAsync, () => !_isBusy);
        UnlockCardCommand = new AsyncRelayCommand(UnlockCardAsync, () => !_isBusy);
        CloseCardCommand = new AsyncRelayCommand(CloseCardAsync, () => !_isBusy);

        TriageCategories = Enum.GetValues<TriageCategory>().ToArray();
        Genders = Enum.GetValues<PatientGender>().ToArray();
        OrderTypes = Enum.GetValues<MedicalOrderType>().ToArray();
    }

    /// <summary>Pacjenci oczekujący na ocenę Triage.</summary>
    public ObservableCollection<PatientCardDto> AwaitingTriage { get; } = new();

    /// <summary>Pacjenci przydzieleni do bieżącej strefy.</summary>
    public ObservableCollection<PatientCardDto> ZonePatients { get; } = new();

    public IReadOnlyList<TriageCategory> TriageCategories { get; }

    public IReadOnlyList<PatientGender> Genders { get; }

    public IReadOnlyList<MedicalOrderType> OrderTypes { get; }

    public string Pesel
    {
        get => _pesel;
        set
        {
            if (SetProperty(ref _pesel, value))
            {
                ApplyPeselDerivedValues(value);
            }
        }
    }

    /// <summary>
    /// Informacja o danych uzupełnionych z numeru PESEL. Numer koduje datę urodzenia i płeć,
    /// więc oba pola są z niego wyprowadzane (BR-18) i użytkownik nie musi ich wpisywać.
    /// Pole puste, gdy numer jest niepełny albo niepoprawny.
    /// </summary>
    public string PeselDerivedHint
    {
        get => _peselDerivedHint;
        private set => SetProperty(ref _peselDerivedHint, value);
    }

    public string FirstName
    {
        get => _firstName;
        set => SetProperty(ref _firstName, value);
    }

    public string LastName
    {
        get => _lastName;
        set => SetProperty(ref _lastName, value);
    }

    /// <summary>Data urodzenia w formacie RRRR-MM-DD (walidowana po stronie domenowej).</summary>
    public string DateOfBirth
    {
        get => _dateOfBirth;
        set => SetProperty(ref _dateOfBirth, value);
    }

    public PatientGender Gender
    {
        get => _gender;
        set => SetProperty(ref _gender, value);
    }

    public string Complaint
    {
        get => _complaint;
        set => SetProperty(ref _complaint, value);
    }

    /// <summary>Kategoria Triage nadana w formularzu oceny wstępnej.</summary>
    public TriageCategory TriageCategory
    {
        get => _triageCategory;
        set => SetProperty(ref _triageCategory, value);
    }

    /// <summary>Uzasadnienie kliniczne — element ścieżki audytowej triage.</summary>
    public string ClinicalJustification
    {
        get => _clinicalJustification;
        set => SetProperty(ref _clinicalJustification, value);
    }

    /// <summary>Podsumowanie parametrów życiowych.</summary>
    public string VitalSigns
    {
        get => _vitalSigns;
        set => SetProperty(ref _vitalSigns, value);
    }

    /// <summary>Pacjent wybrany do oceny Triage z listy oczekujących.</summary>
    public PatientCardDto? SelectedTriagePatient
    {
        get => _selectedTriagePatient;
        set
        {
            if (SetProperty(ref _selectedTriagePatient, value))
            {
                SetZoneAssignmentTarget(value);
            }
        }
    }

    /// <summary>Strefa wybrana do przydzielenia pacjentowi ocenionemu w Triage.</summary>
    public ZoneLoadDto? SelectedZoneForAssignment
    {
        get => _selectedZoneForAssignment;
        set => SetProperty(ref _selectedZoneForAssignment, value);
    }

    /// <summary>Nazwa pacjenta, dla którego wybrano strefę do przydzielenia.</summary>
    public string ZoneAssignmentTargetName =>
        _zoneAssignmentPatientName ?? "wybierz pacjenta z kolejki triage";

    /// <summary>
    /// Karta wybrana na pulpicie — pozwala sprawdzić blokadę przed edycją.
    /// Zmiana zaznaczenia ładuje pełne szczegóły karty: bez tego zdarzenia panel szczegółów
    /// (sterowany przez <see cref="HasSelectedDetails"/>) nigdy nie stałby się widoczny.
    /// </summary>
    public PatientCardDto? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (!SetProperty(ref _selectedPatient, value))
            {
                return;
            }

            SelectedPatientDetails = null;
            SelectPatientCommand.Execute(null);
        }
    }

    /// <summary>Pełne szczegóły karty (okno szczegółów pacjenta).</summary>
    public PatientDetailsDto? SelectedPatientDetails
    {
        get => _selectedPatientDetails;
        private set
        {
            if (SetProperty(ref _selectedPatientDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedDetails));
                OnPropertyChanged(nameof(CanEnterDiagnosis));
                CompleteOrderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedDetails => _selectedPatientDetails is not null;

    /// <summary>
    /// Podmienia zawartość otwartej karty pacjenta. Używane przez widoki zależne
    /// (np. formularz leków), które wykonują operację na tej samej karcie.
    /// </summary>
    public void ShowDetails(PatientDetailsDto details) => SelectedPatientDetails = details;

    /// <summary>BR-13: rozpoznanie ICD-10 wprowadza wyłącznie lekarz lub koordynator.</summary>
    public bool CanEnterDiagnosis => _session.CurrentUser?.CanEnterDiagnosis == true;

    /// <summary>BR-13: zlecenie lekarskie wystawia wyłącznie lekarz lub koordynator.</summary>
    public bool CanIssueOrders => _session.CurrentUser?.CanIssueOrders == true;

    /// <summary>Bieżące obciążenie strefy kontekstowej (pasek postępu pulpitu).</summary>
    public ZoneLoadDto? ZoneLoad
    {
        get => _zoneLoad;
        private set
        {
            if (SetProperty(ref _zoneLoad, value))
            {
                OnPropertyChanged(nameof(ZoneOccupancyPercent));
                OnPropertyChanged(nameof(FreeBeds));
                OnPropertyChanged(nameof(LoadStatusDescription));
            }
        }
    }

    /// <summary>Zajętość strefy w procentach — źródło wizualizacji paska obciążenia.</summary>
    public double ZoneOccupancyPercent => _zoneLoad is null ? 0 : (double)_zoneLoad.LoadRatio * 100;

    public int FreeBeds => _zoneLoad?.FreeBeds ?? 0;

    public string LoadStatusDescription => _zoneLoad?.StatusDescription ?? string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            RegisterPatientCommand.RaiseCanExecuteChanged();
            PerformTriageCommand.RaiseCanExecuteChanged();
            AddOrderCommand.RaiseCanExecuteChanged();
            SetDiagnosisCommand.RaiseCanExecuteChanged();
            LockCardCommand.RaiseCanExecuteChanged();
            UnlockCardCommand.RaiseCanExecuteChanged();
            CloseCardCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RegisterPatientCommand { get; }

    public AsyncRelayCommand PerformTriageCommand { get; }

    /// <summary>Przydziela pacjenta z kolejki triage do wybranej strefy (BR-01a/BR-04) — ścieżka ręczna.</summary>
    public AsyncRelayCommand AssignToZoneCommand { get; }

    public AsyncRelayCommand SelectPatientCommand { get; }

    public AsyncRelayCommand AddOrderCommand { get; }

    /// <summary>
    /// Oznacza wskazane zlecenie jako zrealizowane. Przyjmuje zlecenie jako parametr, dzięki
    /// czemu aktywność przycisku jest liczona osobno dla każdego wiersza listy zleceń.
    /// </summary>
    public AsyncRelayCommand CompleteOrderCommand { get; }

    public AsyncRelayCommand SetDiagnosisCommand { get; }

    public AsyncRelayCommand LockCardCommand { get; }

    public AsyncRelayCommand UnlockCardCommand { get; }

    public AsyncRelayCommand CloseCardCommand { get; }

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

    public string Icd10Code
    {
        get => _icd10Code;
        set => SetProperty(ref _icd10Code, value);
    }

    /// <summary>Odświeża kolejkę triage, listę pacjentów strefy i bieżące obciążenie.</summary>
    public async Task RefreshAsync()
    {
        var zoneId = _session.CurrentZone?.ZoneId ?? _session.CurrentUser?.CurrentZoneId;

        if (zoneId is null)
        {
            return;
        }

        IsBusy = true;

        var selectedPatientId = _selectedPatient?.Id;

        try
        {
            var awaiting = await _patientService.GetAwaitingTriageAsync().ConfigureAwait(true);
            var inZone = await _patientService.GetZonePatientsAsync(zoneId.Value).ConfigureAwait(true);
            var load = await _monitoringService.GetZoneLoadAsync(zoneId.Value).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                Replace(AwaitingTriage, awaiting);
                Replace(ZonePatients, inZone);
                ZoneLoad = load;

                // Odtworzenie zaznaczenia po przebudowie listy — inaczej karta pacjenta
                // zamykałaby się po każdej operacji wywołującej odświeżenie pulpitu.
                if (selectedPatientId is not null)
                {
                    SelectedPatient = ZonePatients.FirstOrDefault(p => p.Id == selectedPatientId.Value);
                }
            }).ConfigureAwait(true);
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

    /// <summary>Rejestracja pacjenta w module wstępnym (BR-03).</summary>
    public async Task RegisterPatientAsync()
    {
        if (!DateOnly.TryParse(_dateOfBirth, out var birthDate))
        {
            StatusMessage = "Podaj datę urodzenia w formacie RRRR-MM-DD.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _patientService.RegisterPatientAsync(
                _pesel.Trim(),
                _firstName.Trim(),
                _lastName.Trim(),
                birthDate,
                _gender,
                string.IsNullOrWhiteSpace(_complaint) ? null : _complaint.Trim(),
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = $"Zarejestrowano pacjenta {patient.FullName}. Nadaj kategorię Triage, aby przydzielić strefę.";
            ClearRegistrationForm();
            await RefreshAsync().ConfigureAwait(true);
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
    /// Wykonuje ocenę Triage (BR-03) i zgodnie z WF-04 przydziela pacjenta do strefy,
    /// w której triage jest wykonywany. W module wstępnym (TRI) przydział pozostaje ręczny
    /// (<see cref="AssignToZoneAsync"/>).
    /// </summary>
    public async Task PerformTriageAsync()
    {
        if (_selectedTriagePatient is null)
        {
            StatusMessage = "Wybierz pacjenta z kolejki oczekujących na triage.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _patientService.PerformTriageAsync(
                _selectedTriagePatient.Id,
                _triageCategory,
                string.IsNullOrWhiteSpace(_clinicalJustification) ? "-" : _clinicalJustification.Trim(),
                string.IsNullOrWhiteSpace(_vitalSigns) ? "-" : _vitalSigns.Trim(),
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = BuildTriageStatusMessage(patient);

            // Pacjent znika z kolejki oczekujących, ale pozostaje celem przydziału strefy,
            // dopóki użytkownik nie wybierze kolejnego pacjenta.
            SetZoneAssignmentTarget(patient.Id, patient.FullName);
            SelectedTriagePatient = null;
            ClinicalJustification = string.Empty;
            VitalSigns = string.Empty;

            await RefreshAsync().ConfigureAwait(true);
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
    /// Komunikat po ocenie Triage. Pacjent triażowany w strefie klinicznej trafia do niej
    /// automatycznie (WF-04). Bez strefy zostaje wtedy, gdy triage wykonano w module wstępnym
    /// albo gdy strefa nie ma wolnego miejsca — w obu przypadkach decyzję podejmuje personel.
    /// </summary>
    private string BuildTriageStatusMessage(PatientDetailsDto patient)
    {
        if (patient.ZoneName is not null)
        {
            return $"Pacjent {patient.FullName} przydzielony do strefy: {patient.ZoneName}.";
        }

        var currentZone = _session.CurrentZone;

        if (currentZone is not null && currentZone.FreeBeds <= 0)
        {
            return $"Pacjent {patient.FullName} oceniony. Strefa {currentZone.ZoneName} jest pełna — przydziel go do innej strefy.";
        }

        return $"Pacjent {patient.FullName} oceniony w module wstępnym. Wybierz strefę i przydziel go do niej.";
    }

    /// <summary>
    /// Przydziela pacjenta po ocenie Triage do wybranej strefy (BR-01a/BR-04). Ścieżka ręczna
    /// służy triage'owi w module wstępnym oraz poprawce przydziału automatycznego.
    /// </summary>
    public async Task AssignToZoneAsync()
    {
        if (_zoneAssignmentPatientId is null)
        {
            StatusMessage = "Wybierz pacjenta z kolejki oczekujących na triage.";
            return;
        }

        if (_selectedZoneForAssignment is null)
        {
            StatusMessage = "Wybierz strefę docelową.";
            return;
        }

        IsBusy = true;

        var selectedZoneForShiftChangeId = _session.SelectedZoneForChange?.ZoneId;

        try
        {
            var patient = await _patientService.AssignToZoneAsync(
                _zoneAssignmentPatientId.Value,
                _selectedZoneForAssignment.ZoneId,
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            // Odświeżenie listy stref po operacji zużywającej wolne miejsce; bez tego
            // ComboBox pokazywałby nieaktualną liczbę wolnych miejsc.
            await _session.LoadZonesAsync().ConfigureAwait(true);
            _session.SelectedZoneForChange = _session.Zones
                .FirstOrDefault(z => z.ZoneId == selectedZoneForShiftChangeId);

            StatusMessage = $"Pacjent {patient.FullName} przydzielony do strefy: {patient.ZoneName}.";

            _zoneAssignmentPatientId = null;
            _zoneAssignmentPatientName = null;
            OnPropertyChanged(nameof(ZoneAssignmentTargetName));

            await RefreshAsync().ConfigureAwait(true);
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

    /// <summary>Wskazuje pacjenta, dla którego wybierana jest strefa, i podpowiada strefę domyślną.</summary>
    private void SetZoneAssignmentTarget(PatientCardDto? patient)
    {
        _zoneAssignmentPatientId = patient?.Id;
        _zoneAssignmentPatientName = patient?.FullName;

        EnsureDefaultZoneSelection();
        OnPropertyChanged(nameof(ZoneAssignmentTargetName));
    }

    private void SetZoneAssignmentTarget(Guid patientId, string patientName)
    {
        _zoneAssignmentPatientId = patientId;
        _zoneAssignmentPatientName = patientName;

        EnsureDefaultZoneSelection();
        OnPropertyChanged(nameof(ZoneAssignmentTargetName));
    }

    /// <summary>
    /// Podpowiada strefę roboczą użytkownika, a gdy jest pełna — strefę z największą liczbą
    /// wolnych miejsc. Wybór pozostaje możliwy ręcznie.
    /// </summary>
    private void EnsureDefaultZoneSelection()
    {
        var zones = _session.Zones;

        if (zones.Count == 0)
        {
            return;
        }

        var current = _session.CurrentZone;

        SelectedZoneForAssignment = current is not null && current.FreeBeds > 0
            ? zones.FirstOrDefault(z => z.ZoneId == current.ZoneId) ?? DefaultZoneWithMostFreeBeds(zones)
            : DefaultZoneWithMostFreeBeds(zones);
    }

    private static ZoneLoadDto? DefaultZoneWithMostFreeBeds(IReadOnlyList<ZoneLoadDto> zones) =>
        zones.Where(z => z.FreeBeds > 0)
            .OrderByDescending(z => z.FreeBeds)
            .ThenBy(z => z.ZoneCode)
            .FirstOrDefault();

    /// <summary>Pobiera pełne dane karty pacjenta do okna szczegółów.</summary>
    public async Task SelectPatientAsync()
    {
        if (_selectedPatient is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var details = await _patientService.GetPatientAsync(_selectedPatient.Id).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() => SelectedPatientDetails = details).ConfigureAwait(true);
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

    /// <summary>Oznacza zlecenie jako zrealizowane i odświeża kartę pacjenta.</summary>
    public async Task CompleteOrderAsync(object? parameter)
    {
        if (parameter is not MedicalOrderDto order)
        {
            return;
        }

        var isFromOpenCard = _selectedPatientDetails?.Orders.Any(item => item.Id == order.Id) == true;

        if (!isFromOpenCard)
        {
            StatusMessage = "Zlecenie dotyczy innej karty pacjenta — wybierz pacjenta ponownie.";
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

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = $"Zlecenie „{order.Description}” oznaczone jako zrealizowane.";

            await _dispatcher.InvokeAsync(() => SelectedPatientDetails = patient).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
            CompleteOrderCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Dodaje zlecenie lekarskie do karty pacjenta.</summary>
    public async Task AddOrderAsync()
    {
        if (_selectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta, dla którego chcesz dodać zlecenie.";
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
                _selectedPatient.Id,
                _orderType,
                _orderDescription.Trim(),
                _orderUrgent,
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = "Zapisano zlecenie.";
            OrderDescription = string.Empty;
            OrderUrgent = false;

            await _dispatcher.InvokeAsync(() => SelectedPatientDetails = patient).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
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

    /// <summary>Ustawia rozpoznanie ICD-10 (tylko lekarz lub koordynator).</summary>
    public async Task SetDiagnosisAsync()
    {
        if (_selectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta.";
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await _patientService
                .SetDiagnosisAsync(_selectedPatient.Id, _icd10Code.Trim(), CancellationToken.None)
                .ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = "Zapisano rozpoznanie.";
            Icd10Code = string.Empty;

            await _dispatcher.InvokeAsync(() => SelectedPatientDetails = patient).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
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

    /// <summary>Przejmuje blokadę karty (BR-20) — chroni przed równoległą edycją.</summary>
    public async Task LockCardAsync()
    {
        await RunPatientOperationAsync(
            id => _patientService.LockCardAsync(id, CancellationToken.None),
            "Przejęto blokadę karty.").ConfigureAwait(true);
    }

    /// <summary>Zwalnia blokadę karty.</summary>
    public async Task UnlockCardAsync()
    {
        await RunPatientOperationAsync(
            id => _patientService.UnlockCardAsync(id, CancellationToken.None),
            "Zwolniono blokadę karty.").ConfigureAwait(true);
    }

    /// <summary>Próba zamknięcia karty pacjenta (BR-09/BR-10/BR-11).</summary>
    public async Task CloseCardAsync()
    {
        await RunPatientOperationAsync(
            id => _patientService.CloseCardAsync(id, transportCompleted: true, CancellationToken.None),
            "Zamknięto kartę pacjenta.").ConfigureAwait(true);
    }

    /// <summary>
    /// Wspólny szkielet operacji na karcie pacjenta: wykonanie, zapis transakcji,
    /// odświeżenie widoku i mapowanie ewentualnych wyjątków domenowych na komunikat.
    /// </summary>
    private async Task RunPatientOperationAsync(
        Func<Guid, Task<PatientDetailsDto>> operation,
        string successMessage)
    {
        if (_selectedPatient is null || _isBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var patient = await operation(_selectedPatient.Id).ConfigureAwait(true);
            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = successMessage;

            await _dispatcher.InvokeAsync(() => SelectedPatientDetails = patient).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
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
    /// Uzupełnia datę urodzenia i płeć na podstawie wpisanego numeru PESEL (BR-18).
    /// Dopóki numer jest niepełny albo niepoprawny, pola zostają bez zmian — wtedy
    /// użytkownik nadal może je wpisać ręcznie, a zgodność sprawdza domena przy rejestracji.
    /// Numer koduje tylko dwie cyfry roku, więc pole daty zostaje edytowalne: pozwala to
    /// poprawić stulecie, którego PESEL nie rozstrzyga.
    /// </summary>
    private void ApplyPeselDerivedValues(string? raw)
    {
        if (!PeselNumber.TryDecode(raw, out var dateOfBirth, out var gender))
        {
            PeselDerivedHint = string.Empty;

            return;
        }

        DateOfBirth = dateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Gender = gender;

        var genderText = gender == PatientGender.Male ? "mężczyzna" : "kobieta";
        PeselDerivedHint = $"Data urodzenia i płeć ({genderText}) uzupełnione z numeru PESEL.";
    }

    private void ClearRegistrationForm()
    {
        Pesel = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        DateOfBirth = string.Empty;
        Gender = PatientGender.Male;
        Complaint = string.Empty;
        PeselDerivedHint = string.Empty;
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}