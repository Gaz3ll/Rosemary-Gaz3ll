using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>
/// Główny ViewModel okna aplikacji. Pełni rolę punktu kompozycji dla widoku:
/// udostępnia kontekst sesji (logowanie, strefa, rotacja), pulpit pacjentów oraz
/// przełącznik motywu, nie zawierając samodzielnie logiki biznesowej.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private string _themeToggleLabel = string.Empty;

    public MainViewModel(
        SessionViewModel session,
        PatientBoardViewModel board,
        MedicationCatalogViewModel catalog,
        IThemeService themeService)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Board = board ?? throw new ArgumentNullException(nameof(board));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        _themeService.ThemeChanged += (_, theme) =>
        {
            ThemeToggleLabel = GetToggleLabel(theme);
            OnPropertyChanged(nameof(IsDarkTheme));
        };

        ThemeToggleLabel = GetToggleLabel(_themeService.Current);

        ToggleThemeCommand = new RelayCommand(
            _ => _themeService.Toggle(),
            _ => true);
    }

    /// <summary>Kontekst sesji: logowanie, strefa robocza, wnioski o rotację (BR-02/BR-05).</summary>
    public SessionViewModel Session { get; }

    /// <summary>Pulpit strefy: rejestracja, triage, karta pacjenta, obciążenie (BR-03/BR-06).</summary>
    public PatientBoardViewModel Board { get; }

    /// <summary>Formularz leków, pakiety medyczne i katalog rozpoznań ICD-10.</summary>
    public MedicationCatalogViewModel Catalog { get; }

    /// <summary>Przełącznik motywu jasnego i ciemnego — dostępny również na ekranie logowania.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>Etykieta przełącznika: „Tryb ciemny" oznacza akcję przejścia na ciemny motyw.</summary>
    public string ThemeToggleLabel
    {
        get => _themeToggleLabel;
        private set => SetProperty(ref _themeToggleLabel, value);
    }

    /// <summary>Czy aktywny jest motyw ciemny.</summary>
    public bool IsDarkTheme => _themeService.Current == AppTheme.Dark;

    private static string GetToggleLabel(AppTheme theme) =>
        theme == AppTheme.Dark ? "Tryb jasny" : "Tryb ciemny";
}
