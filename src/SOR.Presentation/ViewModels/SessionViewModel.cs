using System.Collections.ObjectModel;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Enums;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// ViewModel ekranu logowania oraz globalnego kontekstu pracy.
///
/// Zasada „logowania kontekstowego" oznacza, że po uwierzytelnieniu strefa robocza nie jest
/// wybierana ręcznie przez lekarza — jest wyznaczana automatycznie z aktywnego dyżuru
/// zapisanego w grafiku (BR-02). Użytkownik może jedynie przyjąć proponowaną strefę albo,
/// jeśli ma do tego prawo, poprosić o zmianę, co uruchamia procedurę rotacji zapisywaną
/// w dzienniku audytu wraz z uzasadnieniem (BR-05).
/// </summary>
public sealed class SessionViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IZoneLoadMonitoringService _monitoringService;
    private readonly IStaffRotationService _rotationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UiThreadDispatcher _dispatcher;

    private string _login = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isAuthenticated;
    private AuthenticatedUserDto? _currentUser;
    private ZoneLoadDto? _currentZone;
    private RotationRecommendationDto? _recommendation;
    private ZoneLoadDto? _selectedZoneForChange;
    private ReassignmentReasonCode _selectedReasonCode = ReassignmentReasonCode.ResuscitationSupport;
    private string _reasonComment = string.Empty;

    public SessionViewModel(
        IAuthenticationService authenticationService,
        IZoneLoadMonitoringService monitoringService,
        IStaffRotationService rotationService,
        IUnitOfWork unitOfWork,
        UiThreadDispatcher dispatcher)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _rotationService = rotationService ?? throw new ArgumentNullException(nameof(rotationService));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        LoginCommand = new AsyncRelayCommand(LoginAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        RequestZoneChangeCommand = new AsyncRelayCommand(RequestZoneChangeAsync);
        AcceptRecommendationCommand = new AsyncRelayCommand(AcceptRecommendationAsync, () => _recommendation is not null);
        DismissRecommendationCommand = new RelayCommand(DismissRecommendation);

        Reasons = Enum.GetValues<ReassignmentReasonCode>()
            .Where(r => r != ReassignmentReasonCode.Other)
            .Select(r => new ReasonOption(r, DescribeReason(r)))
            .ToArray();
    }

    /// <summary>Obciążenie wszystkich stref — źródło dla pulpitu i dla okna zmiany strefy.</summary>
    public ObservableCollection<ZoneLoadDto> Zones { get; } = new();

    /// <summary>Uzasadnienia zmiany strefy udostępnione w słowniku (BR-05).</summary>
    public IReadOnlyList<ReasonOption> Reasons { get; }

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    /// <summary>Komunikat błędu (nieprawidłowe dane, brak uprawnień, konto zablokowane).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    /// <summary>Komunikat potwierdzający ostatnią poprawną operację (np. zmianę strefy).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(_statusMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            LoginCommand.RaiseCanExecuteChanged();
            LogoutCommand.RaiseCanExecuteChanged();
            RequestZoneChangeCommand.RaiseCanExecuteChanged();
        }
    }

    public AuthenticatedUserDto? CurrentUser
    {
        get => _currentUser;
        private set
        {
            if (!SetProperty(ref _currentUser, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(CanChangeZone));
            OnPropertyChanged(nameof(IsCoordinator));
        }
    }

    public bool IsAuthenticated => _isAuthenticated && _currentUser is not null;

    public string DisplayName => _currentUser?.DisplayName ?? string.Empty;

    /// <summary>Czy zalogowany użytkownik może wnioskować o zmianę własnej strefy.</summary>
    public bool CanChangeZone => _isAuthenticated && _currentUser is not null;

    /// <summary>Koordynator widzi wszystkie strefy i może rotować innych pracowników.</summary>
    public bool IsCoordinator => _currentUser?.CanManageAllZones == true;

    /// <summary>Strefa kontekstowa wyznaczona z dyżuru — obowiązuje przez całą sesję.</summary>
    public ZoneLoadDto? CurrentZone
    {
        get => _currentZone;
        private set
        {
            if (SetProperty(ref _currentZone, value))
            {
                OnPropertyChanged(nameof(CurrentZoneName));
            }
        }
    }

    public string CurrentZoneName => _currentZone?.ZoneName ?? _currentUser?.CurrentZoneName ?? "brak";

    /// <summary>Rekomendacja rotacji zgłoszona przez monitoring (BR-07).</summary>
    public RotationRecommendationDto? Recommendation
    {
        get => _recommendation;
        private set
        {
            if (SetProperty(ref _recommendation, value))
            {
                OnPropertyChanged(nameof(HasRecommendation));
                OnPropertyChanged(nameof(RecommendationText));
                AcceptRecommendationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasRecommendation => _recommendation is not null;

    public string RecommendationText => _recommendation?.BannerText ?? string.Empty;

    /// <summary>Strefa docelowa wybrana w oknie zmiany kontekstu.</summary>
    public ZoneLoadDto? SelectedZoneForChange
    {
        get => _selectedZoneForChange;
        set
        {
            if (SetProperty(ref _selectedZoneForChange, value))
            {
                RequestZoneChangeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Przyczyna zmiany strefy — wartość słownika, obowiązkowa (BR-05).</summary>
    public ReassignmentReasonCode SelectedReasonCode
    {
        get => _selectedReasonCode;
        set => SetProperty(ref _selectedReasonCode, value);
    }

    /// <summary>Komentarz uzasadnienia; wymagany, gdy wybrano powód „Inny".</summary>
    public string ReasonComment
    {
        get => _reasonComment;
        set => SetProperty(ref _reasonComment, value);
    }

    public AsyncRelayCommand LoginCommand { get; }

    public AsyncRelayCommand LogoutCommand { get; }

    /// <summary>Manualna zmiana strefy przez użytkownika (wymaga uzasadnienia).</summary>
    public AsyncRelayCommand RequestZoneChangeCommand { get; }

    /// <summary>Przyjęcie rekomendacji wygenerowanej przez monitoring.</summary>
    public AsyncRelayCommand AcceptRecommendationCommand { get; }

    /// <summary>Odrzucenie rekomendacji bez zmiany strefy.</summary>
    public RelayCommand DismissRecommendationCommand { get; }

    /// <summary>
    /// Zdarzenie zmiany stanu sesji (zalogowanie / wylogowanie). Widok wykorzystuje je
    /// do pobrania danych referencyjnych formularza dopiero dla aktywnej sesji.
    /// </summary>
    public event EventHandler? SessionStateChanged;

    /// <summary>
    /// Uwierzytelnia użytkownika i ustala strefę kontekstową.
    /// Wyjątki domenowe i aplikacyjne są mapowane na komunikaty dla użytkownika zamiast
    /// być raportowane jako błędy techniczne.
    /// </summary>
    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_login) || string.IsNullOrWhiteSpace(_password))
        {
            ErrorMessage = "Podaj login oraz hasło.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        try
        {
            var result = await _authenticationService
                .LoginAsync(_login.Trim(), _password, CancellationToken.None)
                .ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            CurrentUser = result.User;
            _isAuthenticated = true;
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(CanChangeZone));
            OnPropertyChanged(nameof(IsCoordinator));

            StatusMessage = result.Message;

            await LoadZonesAsync().ConfigureAwait(true);
            ApplySessionContext();

            // Widok ładuje po zalogowaniu dane referencyjne (formularz leków, pakiety),
            // których nie należy pobierać przed uzyskaniem sesji.
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
            Password = string.Empty;
        }
    }

    /// <summary>
    /// Kończy sesję: zwalnia przypisanie ze strefy i czyści kontekst wraz z listą stref,
    /// aby kolejne logowanie nie dziedziczyło widoczności poprzedniego użytkownika (BR-16).
    /// </summary>
    public async Task LogoutAsync()
    {
        if (!_isAuthenticated)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _authenticationService.LogoutAsync(CancellationToken.None).ConfigureAwait(true);
            await _unitOfWork.CommitAsync().ConfigureAwait(true);
            StatusMessage = "Zakończono sesję.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            ClearSession();
            IsBusy = false;
        }
    }

    /// <summary>Przeładowuje strefy i ich bieżące obciążenie.</summary>
    public async Task LoadZonesAsync()
    {
        if (!_isAuthenticated)
        {
            return;
        }

        var snapshot = await _monitoringService.GetSnapshotAsync().ConfigureAwait(true);

        await _dispatcher.InvokeAsync(() =>
        {
            Zones.Clear();

            foreach (var zone in snapshot.Zones.OrderBy(z => z.ZoneCode))
            {
                Zones.Add(zone);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Wykonuje manualną zmianę strefy z obowiązkowym uzasadnieniem.
    /// Serwis rotacji waliduje uprawnienia, dostępność miejsc i zapisuje wpis audytowy;
    /// po udanej zmianie kontekst sesji jest odświeżany, aby widok pracował już w nowej strefie.
    /// </summary>
    public async Task RequestZoneChangeAsync()
    {
        if (_currentUser is null || _selectedZoneForChange is null || _isBusy)
        {
            return;
        }

        if (_selectedReasonCode == ReassignmentReasonCode.Other && string.IsNullOrWhiteSpace(_reasonComment))
        {
            ErrorMessage = "Dla powodu \u201eInny\u201d wymagane jest uzasadnienie tekstowe.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _rotationService.ReassignZoneAsync(
                new ReassignZoneCommand(
                    _currentUser.Id,
                    _selectedZoneForChange.ZoneId,
                    _selectedReasonCode,
                    _reasonComment),
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = result.AuditMessage;
            ReasonComment = string.Empty;

            await _rotationService.GetRotationHistoryAsync(_currentUser.Id).ConfigureAwait(true);
            await LoadZonesAsync().ConfigureAwait(true);
            ApplySessionContext();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Przyjmuje rekomendację rotacji wygenerowaną przez monitoring.</summary>
    public async Task AcceptRecommendationAsync()
    {
        var recommendation = _recommendation;

        if (recommendation is null || _currentUser is null || _isBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _rotationService.AcceptRecommendationAsync(
                new AcceptRotationCommand(
                    recommendation.CandidateUserId,
                    recommendation.TargetZoneId,
                    ReassignmentReasonCode.ResuscitationSupport,
                    recommendation.BannerText),
                CancellationToken.None).ConfigureAwait(true);

            await _unitOfWork.CommitAsync().ConfigureAwait(true);

            StatusMessage = result.AuditMessage;
            Recommendation = null;

            await LoadZonesAsync().ConfigureAwait(true);
            ApplySessionContext();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = ExceptionMessageMapper.Map(exception).Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Odrzuca rekomendację; strefa pozostaje bez zmian.</summary>
    public void DismissRecommendation()
    {
        Recommendation = null;
        StatusMessage = "Rekomendacja odrzucona — strefa bez zmian.";
    }

    /// <summary>
    /// Ustawia strefę kontekstową zgodną z sesją po zalogowaniu lub po rotacji.
    /// Najpierw pobiera najświeższy kontekst z serwisu uwierzytelniania — po rotacji
    /// własnej strefy zmienia się on w bazie, a ViewModel przechowuje jedynie migawkę.
    /// </summary>
    private void ApplySessionContext()
    {
        var sessionUser = _authenticationService.CurrentUser;

        if (sessionUser is not null)
        {
            CurrentUser = sessionUser;
        }

        if (_currentUser is null)
        {
            return;
        }

        CurrentZone = Zones.FirstOrDefault(z => z.ZoneId == _currentUser.CurrentZoneId)
                      ?? Zones.FirstOrDefault();

        SelectedZoneForChange = CurrentZone;
        OnPropertyChanged(nameof(CurrentZoneName));
    }

    /// <summary>Czyści stan sesji po wylogowaniu.</summary>
    private void ClearSession()
    {
        _isAuthenticated = false;
        CurrentUser = null;
        CurrentZone = null;
        Recommendation = null;
        SelectedZoneForChange = null;
        ReasonComment = string.Empty;
        Zones.Clear();

        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CanChangeZone));
        OnPropertyChanged(nameof(IsCoordinator));
        OnPropertyChanged(nameof(HasRecommendation));
        OnPropertyChanged(nameof(CurrentZoneName));

        // Powiadamia widoki zależne od sesji (m.in. formularz leków), aby porzuciły
        // dane poprzedniego użytkownika.
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DescribeReason(ReassignmentReasonCode code) => code switch
    {
        ReassignmentReasonCode.ResuscitationSupport => "Wsparcie resuscytacji",
        ReassignmentReasonCode.EmergencySubstitute => "Zastępstwo nieobecnego",
        ReassignmentReasonCode.TraumaSurge => "Napływ pacjentów urazowych",
        ReassignmentReasonCode.InternalSurge => "Napływ pacjentów internistycznych",
        ReassignmentReasonCode.TriageSupport => "Wsparcie triage",
        ReassignmentReasonCode.ImagingCoordination => "Koordynacja badania obrazowego",
        ReassignmentReasonCode.RegulatedBreak => "Przerwa regulacyjna",
        ReassignmentReasonCode.EquipmentFailure => "Awaria wyposażenia",
        _ => "Inny powód"
    };
}

/// <summary>Pozycja listy rozwijanej z uzasadnieniem zmiany strefy.</summary>
public sealed record ReasonOption(ReassignmentReasonCode Code, string Label);
