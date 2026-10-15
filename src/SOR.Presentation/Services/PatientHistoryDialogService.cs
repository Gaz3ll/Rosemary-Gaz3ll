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
    private readonly SessionViewModel _session;

    public PatientHistoryDialogService(
        IPatientService patientService,
        IClock clock,
        IDepartmentCatalogService departmentCatalog,
        SessionViewModel session)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _departmentCatalog = departmentCatalog ?? throw new ArgumentNullException(nameof(departmentCatalog));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task<bool> ShowAsync(Guid patientId)
    {
        var viewModel = new PatientHistoryViewModel(_patientService, _clock, _departmentCatalog, _session);
        await viewModel.LoadAsync(patientId).ConfigureAwait(true);

        // Dopiero po wczytaniu danych okno trafia na ekran — nie mruga pustymi sekcjami.
        var window = new PatientHistoryWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        window.ShowDialog();

        return viewModel.HasChanges;
    }
}
