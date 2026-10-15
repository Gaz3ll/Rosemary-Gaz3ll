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
    /// <summary>Pokazuje historię pobytu pacjenta; wraca po zamknięciu okna.</summary>
    Task ShowAsync(Guid patientId);
}

/// <summary>Domyślna implementacja: ładuje dane przez serwis pacjenta i pokazuje okno modalne.</summary>
public sealed class PatientHistoryDialogService : IPatientHistoryDialogService
{
    private readonly IPatientService _patientService;
    private readonly IClock _clock;

    public PatientHistoryDialogService(IPatientService patientService, IClock clock)
    {
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ShowAsync(Guid patientId)
    {
        var viewModel = new PatientHistoryViewModel(_patientService, _clock);
        await viewModel.LoadAsync(patientId).ConfigureAwait(true);

        // Dopiero po wczytaniu danych okno trafia na ekran — nie mruga pustymi sekcjami.
        var window = new PatientHistoryWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        window.ShowDialog();
    }
}
