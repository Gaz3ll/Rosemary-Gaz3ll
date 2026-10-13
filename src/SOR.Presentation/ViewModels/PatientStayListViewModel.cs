using System.Collections.ObjectModel;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// ViewModel ekranu po zalogowaniu zgodnego ze specyfikacją interfejsu: górny pasek modułów,
/// panel jednostki organizacyjnej i wyszukiwarki, panel zakresu dat oraz filtrów statusowych,
/// tabela danych i dolny pasek zakładek (Lista pobytów / Raport z dyżuru / Badania obrazowe).
/// </summary>
public sealed class PatientStayListViewModel : ObservableObject
{
    private const string DefaultUnit = "Szpitalny Oddział Ratunkowy";

    private readonly IPatientStayService _stayService;
    private readonly IAuditLogService _auditLog;
    private readonly IClock _clock;
    private readonly UiThreadDispatcher _dispatcher;
    private readonly SessionViewModel _session;

    private string _organizationalUnit = DefaultUnit;
    private string _patientSearch = string.Empty;
    private DateTime _dateFrom = new(2026, 10, 5);
    private DateTime _dateTo = new(2026, 10, 5);
    private StayFilter _statusFilter = StayFilter.All;
    private bool _withoutRefusalCard;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public PatientStayListViewModel(
        IPatientStayService stayService,
        IAuditLogService auditLog,
        IClock clock,
        SessionViewModel session,
        UiThreadDispatcher dispatcher)
    {
        _stayService = stayService ?? throw new ArgumentNullException(nameof(stayService));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        SearchCommand = new AsyncRelayCommand(RefreshAsync, () => !_isBusy);

        // Nawigacja krokowa jest rozdzielona na „datę od” i „datę do”: przyciski w wierszu
        // „Data od” zmieniają wyłącznie dolną granicę, a w wierszu „Data do” — wyłącznie górną.
        PreviousDayFromCommand = new RelayCommand(_ => ShiftFromDays(-1));
        NextDayFromCommand = new RelayCommand(_ => ShiftFromDays(1));
        PreviousMonthFromCommand = new RelayCommand(_ => ShiftFromMonths(-1));
        NextMonthFromCommand = new RelayCommand(_ => ShiftFromMonths(1));
        PreviousYearFromCommand = new RelayCommand(_ => ShiftFromYears(-1));
        NextYearFromCommand = new RelayCommand(_ => ShiftFromYears(1));

        PreviousDayToCommand = new RelayCommand(_ => ShiftToDays(-1));
        NextDayToCommand = new RelayCommand(_ => ShiftToDays(1));
        PreviousMonthToCommand = new RelayCommand(_ => ShiftToMonths(-1));
        NextMonthToCommand = new RelayCommand(_ => ShiftToMonths(1));
        PreviousYearToCommand = new RelayCommand(_ => ShiftToYears(-1));
        NextYearToCommand = new RelayCommand(_ => ShiftToYears(1));
    }

    /// <summary>Jednostki organizacyjne dostępne w filtrze ( SOR jako jednostka macierzysta).</summary>
    public IReadOnlyList<string> OrganizationalUnits { get; } = [DefaultUnit];

    /// <summary>Warianty filtru statusu w układzie zgodnym ze specyfikacją (kolejność radio buttonów).</summary>
    public IReadOnlyList<StayFilter> StatusFilters { get; } =
    [
        StayFilter.All,
        StayFilter.CurrentlyInHospital,
        StayFilter.TransferredToDepartment,
        StayFilter.Cancelled,
        StayFilter.Refused,
        StayFilter.CurrentlyInBay,
        StayFilter.Covid19,
        StayFilter.NoInsurance,
        StayFilter.DischargedWithoutFormalities,
        StayFilter.LedgerVerification,
    ];

    /// <summary>Pobytów wczytanych z bazy (przed zastosowaniem filtrów).</summary>
    public ObservableCollection<PatientStayDto> Stays { get; } = new();

    /// <summary>Wiersze tabeli po zastosowaniu wyszukiwarki i filtru statusu.</summary>
    public ObservableCollection<PatientStayDto> VisibleStays { get; } = new();

    /// <summary>Zlecenia badań obrazowych dla zakładki „Badania obrazowe”.</summary>
    public ObservableCollection<ImagingStudyDto> ImagingStudies { get; } = new();

    /// <summary>Wpisy dziennika audytu dla zakładki „Raport z dyżuru”.</summary>
    public ObservableCollection<AuditLogEntryDto> ShiftReport { get; } = new();

    public AsyncRelayCommand SearchCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden dzień w tył — bez zmiany „daty do”.</summary>
    public RelayCommand PreviousDayFromCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden dzień w przód — o ile nie przekroczy „daty do”.</summary>
    public RelayCommand NextDayFromCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden miesiąc w tył — bez zmiany „daty do”.</summary>
    public RelayCommand PreviousMonthFromCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden miesiąc w przód — o ile nie przekroczy „daty do”.</summary>
    public RelayCommand NextMonthFromCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden rok w tył — bez zmiany „daty do”.</summary>
    public RelayCommand PreviousYearFromCommand { get; }

    /// <summary>Przesuwa „datę od” o jeden rok w przód — o ile nie przekroczy „daty do”.</summary>
    public RelayCommand NextYearFromCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden dzień w tył — o ile nie zejdzie poniżej „daty od”.</summary>
    public RelayCommand PreviousDayToCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden dzień w przód — bez zmiany „daty od”.</summary>
    public RelayCommand NextDayToCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden miesiąc w tył — o ile nie zejdzie poniżej „daty od”.</summary>
    public RelayCommand PreviousMonthToCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden miesiąc w przód — bez zmiany „daty od”.</summary>
    public RelayCommand NextMonthToCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden rok w tył — o ile nie zejdzie poniżej „daty od”.</summary>
    public RelayCommand PreviousYearToCommand { get; }

    /// <summary>Przesuwa „datę do” o jeden rok w przód — bez zmiany „daty od”.</summary>
    public RelayCommand NextYearToCommand { get; }

    public string OrganizationalUnit
    {
        get => _organizationalUnit;
        set => SetProperty(ref _organizationalUnit, value);
    }

    /// <summary>Tekst wyszukiwarki pacjenta (PESEL lub nazwisko).</summary>
    public string PatientSearch
    {
        get => _patientSearch;
        set
        {
            if (SetProperty(ref _patientSearch, value))
            {
                ApplyFilters();
            }
        }
    }

    public DateTime DateFrom
    {
        get => _dateFrom;
        set
        {
            // Wpisana „data od” nie może być późniejsza niż „data do” — w razie próby
            // przyjmujemy wartość „daty do”, czyli obie daty zrównują się.
            var normalized = value > _dateTo ? _dateTo : value;
            var changed = SetProperty(ref _dateFrom, normalized);

            if (!changed && value != normalized)
            {
                // Odrzucona wartość nie zmieniła modelu, ale pole tekstowe DatePickera
                // trzeba cofnąć do obowiązującej daty — stąd wymuszone powiadomienie.
                OnPropertyChanged(nameof(DateFrom));
            }

            if (changed)
            {
                SearchCommand.Execute(null);
            }
        }
    }

    public DateTime DateTo
    {
        get => _dateTo;
        set
        {
            // Wpisana „data do” nie może być wcześniejsza niż „data od” — w razie próby
            // przyjmujemy wartość „daty od”, czyli obie daty zrównują się.
            var normalized = value < _dateFrom ? _dateFrom : value;
            var changed = SetProperty(ref _dateTo, normalized);

            if (!changed && value != normalized)
            {
                OnPropertyChanged(nameof(DateTo));
            }

            if (changed)
            {
                SearchCommand.Execute(null);
            }
        }
    }

    /// <summary>Wybrany filtr statusu (radio button); zmiana odświeża listę.</summary>
    public StayFilter StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
            {
                SearchCommand.Execute(null);
            }
        }
    }

    /// <summary>Dodatkowy checkbox „Brak karty odmowy”.</summary>
    public bool WithoutRefusalCard
    {
        get => _withoutRefusalCard;
        set
        {
            if (SetProperty(ref _withoutRefusalCard, value))
            {
                SearchCommand.Execute(null);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Liczba wierszy po zastosowaniu filtrów — informacja dla użytkownika i dla audytu.</summary>
    public string Summary => $"Pobytów w zakresie: {VisibleStays.Count} / {Stays.Count}";

    /// <summary>Odświeża listę pobytów, raport z dyżuru i badania obrazowe.</summary>
    public async Task RefreshAsync()
    {
        // Po wylogowaniu (lub jeszcze przed logowaniem) nie ma sensu pytać o bazę,
        // a sesja zgłasza zmianę stanu także przy zamykaniu aplikacji.
        if (!_session.IsAuthenticated || _isBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var query = BuildQuery();

            var stays = await _stayService.GetStaysAsync(query, _clock.UtcNow).ConfigureAwait(true);
            var imaging = await _stayService.GetImagingStudiesAsync(query, _clock.UtcNow).ConfigureAwait(true);
            var report = await _auditLog.GetRecentAsync(200).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                Replace(Stays, stays);
                Replace(ImagingStudies, imaging);
                Replace(ShiftReport, report);
                ApplyFilters();
            }).ConfigureAwait(true);

            StatusMessage = string.IsNullOrWhiteSpace(OrganizationalUnit)
                ? "Wybierz jednostkę organizacyjną."
                : $"{OrganizationalUnit}: {stays.Count} pobytów w zakresie {DateFrom:dd.MM.yyyy} – {DateTo:dd.MM.yyyy}.";

            if (IsFilterWithoutDataSupport(StatusFilter))
            {
                StatusMessage += " Wybrany filtr nie ma odpowiednika w modelu domenowym — lista jest pusta.";
            }

            if (WithoutRefusalCard)
            {
                StatusMessage += " „Brak karty odmowy” nie jest jeszcze przechowywany w systemie.";
            }

            OnPropertyChanged(nameof(Summary));
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

    /// <summary>Buduje zapytanie z formularza filtrów (lokalna strefa czasu → UTC).</summary>
    private StayQuery BuildQuery()
    {
        // Wpis rejestracji pacjenta jest przechowywany w UTC, dlatego zakres dobowy
        // (od 00:00 do 23:59:59,9999999) przeliczamy na UTC dla lokalnej strefy czasowej.
        var from = DateFrom.Date;
        var to = DateTo.Date.AddDays(1).AddTicks(-1);

        return new StayQuery(
            new DateTimeOffset(from).ToUniversalTime(),
            new DateTimeOffset(to).ToUniversalTime(),
            StatusFilter,
            WithoutRefusalCard);
    }

    /// <summary>Filtry bez odpowiednika w modelu domenowym zwracają pustą listę.</summary>
    private static bool IsFilterWithoutDataSupport(StayFilter filter) => filter is StayFilter.Refused
        or StayFilter.Covid19
        or StayFilter.NoInsurance
        or StayFilter.DischargedWithoutFormalities
        or StayFilter.LedgerVerification;

    private void ApplyFilters()
    {
        var term = _patientSearch.Trim();

        var filtered = Stays.Where(stay =>
        {
            if (term.Length > 0 &&
                !stay.PatientName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !stay.Pesel.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return StatusFilter switch
            {
                StayFilter.All => true,
                StayFilter.CurrentlyInHospital => stay.DischargedAtUtc is null,
                StayFilter.TransferredToDepartment => stay.DischargeType == DischargeType.TransferToDepartment,
                StayFilter.Cancelled => stay.DischargeType == DischargeType.AtPatientRequest,
                StayFilter.CurrentlyInBay => stay.State == PatientState.Triaged && stay.DischargedAtUtc is null,
                _ => false,
            };
        });

        Replace(VisibleStays, filtered);
        OnPropertyChanged(nameof(Summary));
    }

    private void ShiftFromDays(int offset)
    {
        var candidate = DateFrom.AddDays(offset);

        // Przesunięcie „daty od” w przód nie może przekroczyć „daty do” — wtedy brak efektu.
        if (candidate > DateTo)
        {
            return;
        }

        DateFrom = candidate;
    }

    private void ShiftFromMonths(int offset)
    {
        var candidate = DateFrom.AddMonths(offset);

        if (candidate > DateTo)
        {
            return;
        }

        DateFrom = candidate;
    }

    private void ShiftFromYears(int offset)
    {
        var candidate = DateFrom.AddYears(offset);

        if (candidate > DateTo)
        {
            return;
        }

        DateFrom = candidate;
    }

    private void ShiftToDays(int offset)
    {
        var candidate = DateTo.AddDays(offset);

        // Przesunięcie „daty do” w tył nie może zejść poniżej „daty od” — wtedy brak efektu.
        if (candidate < DateFrom)
        {
            return;
        }

        DateTo = candidate;
    }

    private void ShiftToMonths(int offset)
    {
        var candidate = DateTo.AddMonths(offset);

        if (candidate < DateFrom)
        {
            return;
        }

        DateTo = candidate;
    }

    private void ShiftToYears(int offset)
    {
        var candidate = DateTo.AddYears(offset);

        if (candidate < DateFrom)
        {
            return;
        }

        DateTo = candidate;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}