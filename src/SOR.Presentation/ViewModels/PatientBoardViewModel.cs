using System.Collections.ObjectModel;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Enums;
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
    private string _complaint = string.Empty;
    private TriageCategory _triageCategory = TriageCategory.Blue;
    private string _clinicalJustification = string.Empty;
    private string _vitalSigns = string.Empty;
    private PatientCardDto? _selectedTriagePatient;
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
        SelectPatientCommand = new AsyncRelayCommand(SelectPatientAsync);
        AddOrderCommand = new AsyncRelayCommand(AddOrderAsync, () => !_isBusy);
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
        set => SetProperty(ref _pesel, value);
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
        set => SetProperty(ref _selectedTriagePatient, value);
    }

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

    public AsyncRelayCommand SelectPatientCommand { get; }

    public AsyncRelayCommand AddOrderCommand { get; }

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
    /// Wykonuje ocenę Triage i automatycznie przydziela pacjenta do strefy zgodnie z kalendarzem
    /// Triage oraz aktualną zdolnością przyjęciową (BR-03/BR-04).
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

            StatusMessage = $"Pacjent {patient.FullName} przydzielony do strefy: {patient.ZoneName ?? "brak"}.";
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

    private void ClearRegistrationForm()
    {
        Pesel = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        DateOfBirth = string.Empty;
        Complaint = string.Empty;
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