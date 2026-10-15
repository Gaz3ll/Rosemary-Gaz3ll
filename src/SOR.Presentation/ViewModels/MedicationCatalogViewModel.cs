using System.Collections.ObjectModel;
using System.Windows.Input;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Enums;
using SOR.Presentation.Converters;
using SOR.Presentation.Mvvm;
using SOR.Presentation.Services;

namespace SOR.Presentation.ViewModels;

/// <summary>Pozycja filtra grupy terapeutycznej — etykieta jest zawsze polska.</summary>
public sealed record MedicationCategoryFilterOption(MedicationCategory? Category, string Label);

/// <summary>
/// Formularz leków oddziału: wyszukanie preparatu oraz złożenie zlecenia lub odnotowanie
/// podania na karcie pacjenta. Pakiety medyczne i katalog rozpoznań ICD-10 przeniesiono do
/// okna „Historia pobytu” (BR-13). Operacje wykonują serwisy aplikacyjne, a komunikaty
/// błędów trafiają na pasek statusu pulpitu.
/// </summary>
public sealed class MedicationCatalogViewModel : ObservableObject
{
    private readonly IMedicationCatalogService _medicationCatalog;
    private readonly IPatientService _patientService;
    private readonly PatientBoardViewModel _board;
    private readonly UiThreadDispatcher _dispatcher;

    private string _medicationTerm = string.Empty;
    private MedicationDto? _selectedMedication;
    private string _administrationDose = string.Empty;
    private string _administrationNotes = string.Empty;
    private MedicationCategoryFilterOption _medicationCategoryFilter;

    private string _statusMessage = string.Empty;

    public MedicationCatalogViewModel(
        IMedicationCatalogService medicationCatalog,
        IPatientService patientService,
        PatientBoardViewModel board,
        UiThreadDispatcher dispatcher)
    {
        _medicationCatalog = medicationCatalog ?? throw new ArgumentNullException(nameof(medicationCatalog));
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _medicationCategoryFilter = MedicationCategoryFilters[0];

        SearchMedicationsCommand = new AsyncRelayCommand(SearchMedicationsAsync);
        OrderSelectedMedicationCommand = new AsyncRelayCommand(OrderSelectedMedicationAsync, () => !_board.IsBusy);
        RecordAdministrationCommand = new AsyncRelayCommand(RecordAdministrationAsync, () => !_board.IsBusy);
    }

    /// <summary>Preparaty z formularza oddziału (pełny formularz lub wynik wyszukiwania).</summary>
    public ObservableCollection<MedicationDto> Medications { get; } = new();

    /// <summary>Grupy terapeutyczne do filtrowania formularza (pierwsza pozycja: wszystkie leki).</summary>
    public IReadOnlyList<MedicationCategoryFilterOption> MedicationCategoryFilters { get; } =
    [
        new MedicationCategoryFilterOption(null, "Wszystkie grupy"),
        .. Enum.GetValues<MedicationCategory>()
            .Select(c => new MedicationCategoryFilterOption(c, PolishEnumConverter.ToPolish(c))),
    ];

    public IReadOnlyList<MedicationRoute> Routes { get; } = Enum.GetValues<MedicationRoute>();

    public string MedicationTerm
    {
        get => _medicationTerm;
        set => SetProperty(ref _medicationTerm, value);
    }

    public MedicationCategoryFilterOption MedicationCategoryFilter
    {
        get => _medicationCategoryFilter;
        set => SetProperty(ref _medicationCategoryFilter, value);
    }

    public MedicationDto? SelectedMedication
    {
        get => _selectedMedication;
        set
        {
            if (SetProperty(ref _selectedMedication, value))
            {
                // Dawka domyślnie odpowiada pozycji katalogowej — lekarz może ją skorygować,
                // ale najczęstszy scenariusz to podanie dawki typowej.
                AdministrationDose = value?.TypicalDose ?? string.Empty;
                OnPropertyChanged(nameof(SelectedMedicationWarnings));
                OrderSelectedMedicationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Przeciwwskazania i uwagi wybranego preparatu ( ostrzeżenie dla personelu).</summary>
    public string SelectedMedicationWarnings
    {
        get
        {
            if (_selectedMedication is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (_selectedMedication.RequiresCloseMonitoring)
            {
                parts.Add("LEK PODWYŻSZONEGO RYZYKA — zweryfikuj dawkę i drogę podania przed podaniem.");
            }

            if (!string.IsNullOrWhiteSpace(_selectedMedication.Contraindications))
            {
                parts.Add($"Przeciwwskazania: {_selectedMedication.Contraindications}");
            }

            if (!string.IsNullOrWhiteSpace(_selectedMedication.Notes))
            {
                parts.Add($"Uwagi: {_selectedMedication.Notes}");
            }

            return string.Join(Environment.NewLine, parts);
        }
    }

    public string AdministrationDose
    {
        get => _administrationDose;
        set => SetProperty(ref _administrationDose, value);
    }

    public string AdministrationNotes
    {
        get => _administrationNotes;
        set => SetProperty(ref _administrationNotes, value);
    }

    /// <summary>Komunikat o wyniku ostatniej operacji (widoczny na pasku statusu katalogu).</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Czy wybrano pacjenta — bez tego nie można złożyć zlecenia ani odnotować podania.</summary>
    public bool HasSelectedPatient => _board.SelectedPatientDetails is not null;

    public AsyncRelayCommand SearchMedicationsCommand { get; }

    public AsyncRelayCommand OrderSelectedMedicationCommand { get; }

    public AsyncRelayCommand RecordAdministrationCommand { get; }

    /// <summary>Wczytuje formularz leków — wywoływane po zalogowaniu.</summary>
    public async Task InitializeAsync()
    {
        await SearchMedicationsAsync().ConfigureAwait(true);
    }

    /// <summary>Czyści formularz po zakończeniu sesji — nie zostawia danych poprzedniego użytkownika.</summary>
    public void Clear()
    {
        Medications.Clear();
        SelectedMedication = null;
        AdministrationDose = string.Empty;
        AdministrationNotes = string.Empty;
        StatusMessage = string.Empty;
    }

    private async Task SearchMedicationsAsync()
    {
        try
        {
            var result = await _medicationCatalog
                .SearchAsync(MedicationTerm, MedicationCategoryFilter.Category, CancellationToken.None)
                .ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                Replace(Medications, result);
                StatusMessage = $"Znaleziono {result.Count} pozycji w formularzu.";
            }).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task OrderSelectedMedicationAsync()
    {
        if (_selectedMedication is null)
        {
            StatusMessage = "Wybierz preparat z formularza.";
            return;
        }

        if (_board.SelectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta na pulpicie, aby złożyć zlecenie.";
            return;
        }

        try
        {
            var dose = string.IsNullOrWhiteSpace(_administrationDose)
                ? _selectedMedication.TypicalDose
                : _administrationDose.Trim();

            var patient = await _patientService.AddOrderAsync(
                _board.SelectedPatient.Id,
                MedicalOrderType.Medication,
                $"{_selectedMedication.Name} {_selectedMedication.Strength} — {dose} " +
                $"({PolishEnumConverter.ToPolish(_selectedMedication.Route)})",
                isUrgent: _selectedMedication.RequiresCloseMonitoring,
                CancellationToken.None).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() => _board.ShowDetails(patient)).ConfigureAwait(true);

            StatusMessage = $"Zapisano zlecenie podania {_selectedMedication.DisplayName}.";
            await _board.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task RecordAdministrationAsync()
    {
        if (_selectedMedication is null)
        {
            StatusMessage = "Wybierz preparat z formularza.";
            return;
        }

        if (_board.SelectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta na pulpicie, aby odnotować podanie.";
            return;
        }

        try
        {
            var dose = string.IsNullOrWhiteSpace(_administrationDose)
                ? _selectedMedication.TypicalDose
                : _administrationDose.Trim();

            var patient = await _patientService.RecordMedicationAdministrationAsync(
                _board.SelectedPatient.Id,
                _selectedMedication.Id,
                dose,
                _selectedMedication.Route,
                medicalOrderId: null,
                notes: string.IsNullOrWhiteSpace(_administrationNotes) ? null : _administrationNotes.Trim(),
                cancellationToken: CancellationToken.None).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() => _board.ShowDetails(patient)).ConfigureAwait(true);

            StatusMessage = $"Odnotowano podanie {_selectedMedication.DisplayName} w dawce {dose}.";
            AdministrationNotes = string.Empty;

            await _board.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
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


