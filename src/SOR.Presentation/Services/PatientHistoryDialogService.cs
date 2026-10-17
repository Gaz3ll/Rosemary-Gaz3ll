using System.Windows;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Presentation.ViewModels;

namespace SOR.Presentation.Services;

/// <summary>
/// Otwiera modalne okno „Historia pobytu” dla wskazanego pacjenta. Wydzielone z ViewModeli,
/// aby listy pacjentów nie tworzyły okien bezpośrednio i pozostały testowalne.
/// </summary>
public interface IPatientHistoryDialogService
{
    /// <summary>
    /// Pokazuje historię pobytu pacjenta i wraca po zamknięciu okna. Wynik informuje, czy
    /// w oknie zmieniono kartę (zlecenie, rozpoznanie, wypis) — wtedy wołający odświeża listę.
    /// </summary>
    Task<bool> ShowAsync(Guid patientId);
}

/// <summary>Domyślna implementacja: ładuje dane przez serwis pacjenta i pokazuje okno modalne.</summary>
public sealed class PatientHistoryDialogService : IPatientHistoryDialogService
{
    private readonly IPatientService _patientService;
    private readonly IClock _clock;
    private readonly IDepartmentCatalogService _departmentCatalog;
    private readonly IMedicalBundleService _bundleService;
    private readonly IIcd10CatalogService _icd10Catalog;
    private readonly IThemeService _themeService;
    private readonly SessionViewModel _session;

    public PatientHistoryDialogService(
        IPatientService patientService,
        IClock clock,
        IDepartmentCatalogService departmentCatalog,
        IMedicalBundleService bundleService,
        IIcd10CatalogService icd10Catalog,
        IThemeService themeService,
        SessionViewModel session)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _departmentCatalog = departmentCatalog ?? throw new ArgumentNullException(nameof(departmentCatalog));
        _bundleService = bundleService ?? throw new ArgumentNullException(nameof(bundleService));
        _icd10Catalog = icd10Catalog ?? throw new ArgumentNullException(nameof(icd10Catalog));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<bool> ShowAsync(Guid patientId)
    {
        var viewModel = new PatientHistoryViewModel(
            _patientService,
            _clock,
            _departmentCatalog,
            _bundleService,
            _icd10Catalog,
            _session);

        await viewModel.LoadAsync(patientId).ConfigureAwait(true);

        // Dopiero po wczytaniu danych okno trafia na ekran — nie mruga pustymi sekcjami.
        var window = new PatientHistoryWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        // Natywny pasek tytułowy okna (min/max/close) barwimy tak samo jak okna głównego,
        // a przez czas otwartego dialogu reagujemy na przełączenie motywu w aplikacji.
        void OnThemeChanged(object? sender, AppTheme theme) => WindowTitleBarTheme.Apply(window, theme);

        WindowTitleBarTheme.Apply(window, _themeService.Current);
        _themeService.ThemeChanged += OnThemeChanged;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            _themeService.ThemeChanged -= OnThemeChanged;
        }

        return viewModel.HasChanges;
    }
}
