using SOR.Domain.Enums;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// Główny ViewModel okna aplikacji. Pełni rolę punktu kompozycji dla widoku:
/// udostępnia kontekst sesji (logowanie, strefa, rotacja), pulpit pacjentów, listę pobytów
/// oraz przełącznik motywu, nie zawierając samodzielnie logiki biznesowej.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Zakładki modułowe górnego paska (specyfikacja interfejsu, sekcja A).</summary>
    public enum WorkspaceSection
    {
        /// <summary>PRZYJĘCIA — rejestracja i karta pobytu (domyślna po zalogowaniu).</summary>
        Admissions = 0,

        /// <summary>PORADNIA — moduł poradni, brak danych w modelu domenowym.</summary>
        Clinic = 1,

        /// <summary>INNE — moduły kliniczne (pulpit strefy, formularz leków) i profil użytkownika.</summary>
        Other = 2,
    }

    private readonly IThemeService _themeService;
    private string _themeToggleLabel = string.Empty;
    private WorkspaceSection _section = WorkspaceSection.Admissions;
    private int _selectedStayTabIndex;
    private string _toolbarMessage = string.Empty;

    public MainViewModel(
        SessionViewModel session,
        PatientBoardViewModel board,
        MedicationCatalogViewModel catalog,
        PatientStayListViewModel stays,
        IThemeService themeService)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Stays = stays ?? throw new ArgumentNullException(nameof(stays));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        _themeService.ThemeChanged += (_, theme) =>
        {
            ThemeToggleLabel = GetToggleLabel(theme);
            OnPropertyChanged(nameof(IsDarkTheme));
        };

        ThemeToggleLabel = GetToggleLabel(_themeService.Current);

        // Zmiana sesji odświeża listę pobytów oraz etykietę zalogowanego użytkownika.
        session.SessionStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(UserLabel));
            OnPropertyChanged(nameof(WorkZoneLabel));

            _ = stays.RefreshAsync();
        };

        ToggleThemeCommand = new RelayCommand(
            _ => _themeService.Toggle(),
            _ => true);

        ShowAdmissionsCommand = new RelayCommand(_ => SelectSection(WorkspaceSection.Admissions));
        ShowClinicCommand = new RelayCommand(_ => SelectSection(WorkspaceSection.Clinic));
        ShowOtherCommand = new RelayCommand(_ => SelectSection(WorkspaceSection.Other));

        // Pasek ikon operacyjnych (specyfikacja, sekcja A — poziom dolny).
        SearchCommand = stays.SearchCommand;
        ShowStayCardCommand = new RelayCommand(_ =>
        {
            SelectSection(WorkspaceSection.Admissions);
            SelectedStayTabIndex = 0;
        });
        ShowShiftReportCommand = new RelayCommand(_ =>
        {
            SelectSection(WorkspaceSection.Admissions);
            SelectedStayTabIndex = 1;
        });
        ShowImagingCommand = new RelayCommand(_ =>
        {
            SelectSection(WorkspaceSection.Admissions);
            SelectedStayTabIndex = 2;
        });
        ShowConsultationsCommand = new RelayCommand(_ =>
            ShowNotImplementedModule("Konsultacje"));
        ShowReferralRequestCommand = new RelayCommand(_ =>
            ShowNotImplementedModule("Wniosek"));
        ShowRepresentativeCommand = new RelayCommand(_ =>
            ShowNotImplementedModule("Przedstawiciel / Uprawnienia"));
        ShowAmkCommand = new RelayCommand(_ =>
            ShowNotImplementedModule("AMK"));
        ExitCommand = new RelayCommand(
            _ => _ = Session.LogoutAsync(),
            _ => true);
    }

    /// <summary>Kontekst sesji: logowanie, strefa robocza, wnioski o rotację (BR-02/BR-05).</summary>
    public SessionViewModel Session { get; }

    /// <summary>Pulpit strefy: rejestracja, triage, karta pacjenta, obciążenie (BR-03/BR-06).</summary>
    public PatientBoardViewModel Board { get; }

    /// <summary>Formularz leków, pakiety medyczne i katalog rozpoznań ICD-10.</summary>
    public MedicationCatalogViewModel Catalog { get; }

    /// <summary>Lista pobytów: jednostka organizacyjna, wyszukiwarka, zakres dat, filtry i tabela danych.</summary>
    public PatientStayListViewModel Stays { get; }

    /// <summary>Przełącznik motywu jasnego i ciemnego — dostępny również na ekranie logowania.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>Zakładka PRZYJĘCIA (domyślna): tabela pobytów z filtrami.</summary>
    public RelayCommand ShowAdmissionsCommand { get; }

    /// <summary>Zakładka PORADNIA.</summary>
    public RelayCommand ShowClinicCommand { get; }

    /// <summary>Zakładka INNE: moduły kliniczne i profil użytkownika.</summary>
    public RelayCommand ShowOtherCommand { get; }

    /// <summary>Ikona „Szukaj” — odświeża listę pobytów.</summary>
    public AsyncRelayCommand SearchCommand { get; }

    /// <summary>Ikona „Karta pobytu” — zakładka listy pobytów.</summary>
    public RelayCommand ShowStayCardCommand { get; }

    /// <summary>Ikona „Raport” — zakładka raportu z dyżuru.</summary>
    public RelayCommand ShowShiftReportCommand { get; }

    /// <summary>Ikona „Badania obrazowe”.</summary>
    public RelayCommand ShowImagingCommand { get; }

    /// <summary>Ikona „Konsultacje”.</summary>
    public RelayCommand ShowConsultationsCommand { get; }

    /// <summary>Ikona „Wniosek”.</summary>
    public RelayCommand ShowReferralRequestCommand { get; }

    /// <summary>Ikona „Przedstawiciel / Uprawnienia”.</summary>
    public RelayCommand ShowRepresentativeCommand { get; }

    /// <summary>Ikona „AMK” (zlecenia dla zespołu ratownictwa medycznego).</summary>
    public RelayCommand ShowAmkCommand { get; }

    /// <summary>Ikona „Wyjdź” — wylogowanie z aplikacji.</summary>
    public RelayCommand ExitCommand { get; }

    /// <summary>Aktywna zakładka modułowa górnego paska.</summary>
    public WorkspaceSection Section
    {
        get => _section;
        private set
        {
            if (SetProperty(ref _section, value))
            {
                OnPropertyChanged(nameof(IsAdmissionsSelected));
                OnPropertyChanged(nameof(IsClinicSelected));
                OnPropertyChanged(nameof(IsOtherSelected));
                OnPropertyChanged(nameof(IsAdmissionsVisible));
                OnPropertyChanged(nameof(IsClinicVisible));
                OnPropertyChanged(nameof(IsOtherVisible));
            }
        }
    }

    /// <summary>Indeks aktywnej zakładki w obszarze danych (0 — karta pobytu, 1 — raport, 2 — badania).</summary>
    public int SelectedStayTabIndex
    {
        get => _selectedStayTabIndex;
        private set => SetProperty(ref _selectedStayTabIndex, value);
    }

    /// <summary>Komunikat paska statusu dla modułów bez danych w modelu domenowym.</summary>
    public string ToolbarMessage
    {
        get => _toolbarMessage;
        private set
        {
            if (SetProperty(ref _toolbarMessage, value))
            {
                OnPropertyChanged(nameof(HasToolbarMessage));
            }
        }
    }

    /// <summary>Czy widoczny jest komunikat paska ikon o niezaimplementowanym module.</summary>
    public bool HasToolbarMessage => !string.IsNullOrWhiteSpace(ToolbarMessage);

    public bool IsAdmissionsSelected => Section == WorkspaceSection.Admissions;

    public bool IsClinicSelected => Section == WorkspaceSection.Clinic;

    public bool IsOtherSelected => Section == WorkspaceSection.Other;

    public bool IsAdmissionsVisible => Section == WorkspaceSection.Admissions;

    public bool IsClinicVisible => Section == WorkspaceSection.Clinic;

    public bool IsOtherVisible => Section == WorkspaceSection.Other;

    /// <summary>Etykieta zalogowanego użytkownika w formacie „LOGIN NAZWISKO_IMIĘ (ROLA)”.</summary>
    public string UserLabel
    {
        get
        {
            var user = Session.CurrentUser;

            if (user is null)
            {
                return string.Empty;
            }

            return $"{user.Login.ToUpperInvariant()} {user.DisplayName.ToUpperInvariant()} ({GetRoleLabel(user.Role)})";
        }
    }

    /// <summary>Strefa robocza użytkownika — pokazywana obok etykiety na pasku.</summary>
    public string WorkZoneLabel =>
        $"Strefa: {Session.CurrentZoneName.ToUpperInvariant()}";

    /// <summary>Etykieta przełącznika: „Tryb ciemny" oznacza akcję przejścia na ciemny motyw.</summary>
    public string ThemeToggleLabel
    {
        get => _themeToggleLabel;
        private set => SetProperty(ref _themeToggleLabel, value);
    }

    /// <summary>Czy aktywny jest motyw ciemny.</summary>
    public bool IsDarkTheme => _themeService.Current == AppTheme.Dark;

    private void SelectSection(WorkspaceSection section)
    {
        Section = section;
        ToolbarMessage = string.Empty;
    }

    private void ShowNotImplementedModule(string moduleName) =>
        ToolbarMessage = $"Moduł „{moduleName}” nie jest zaimplementowany w tym etapie systemu.";

    private static string GetToggleLabel(AppTheme theme) =>
        theme == AppTheme.Dark ? "Tryb jasny" : "Tryb ciemny";

    /// <summary>Nazwa rola użytkownika w formacie wymaganym przez specyfikację.</summary>
    private static string GetRoleLabel(UserRole role) => role switch
    {
        UserRole.Physician => "LEKARZ",
        UserRole.Nurse => "PIELĘGNIARKA",
        UserRole.Coordinator => "KOORDYNATOR",
        UserRole.Paramedic => "RATOWNIK MEDYCZNY",
        _ => "UŻYTKOWNIK",
    };
}