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
/// Katalog leków, pakiety medyczne i katalog rozpoznań ICD-10.
///
/// <para>
/// ViewModel łączy trzy dane referencyjne SOR z kartą pacjenta: wyszukanie preparatu,
/// złożenie zlecenia lub odnotowanie podania oraz zastosowanie gotowego pakietu
/// postępowania. Wszystkie operacje są wykonywane przez serwisy aplikacyjne,
/// a komunikaty błędów trafiają na pasek statusu pulpitu.
/// </para>
/// </summary>
public sealed class MedicationCatalogViewModel : ObservableObject
{
    private readonly IMedicationCatalogService _medicationCatalog;
    private readonly IMedicalBundleService _bundleService;
    private readonly IIcd10CatalogService _icd10Catalog;
    private readonly IPatientService _patientService;
    private readonly PatientBoardViewModel _board;
    private readonly UiThreadDispatcher _dispatcher;

    private string _medicationTerm = string.Empty;
    private MedicationDto? _selectedMedication;
    private string _administrationDose = string.Empty;
    private string _administrationNotes = string.Empty;
    private MedicationCategoryFilterOption _medicationCategoryFilter;

    private string _bundleTerm = string.Empty;
    private MedicalBundleDto? _selectedBundle;

    private string _icd10Term = string.Empty;
    private Icd10CatalogEntryDto? _selectedDiagnosis;
    private string _icd10Code = string.Empty;

    private string _statusMessage = string.Empty;

    public MedicationCatalogViewModel(
        IMedicationCatalogService medicationCatalog,
        IMedicalBundleService bundleService,
        IIcd10CatalogService icd10Catalog,
        IPatientService patientService,
        PatientBoardViewModel board,
        UiThreadDispatcher dispatcher)
    {
        _medicationCatalog = medicationCatalog ?? throw new ArgumentNullException(nameof(medicationCatalog));
        _bundleService = bundleService ?? throw new ArgumentNullException(nameof(bundleService));
        _icd10Catalog = icd10Catalog ?? throw new ArgumentNullException(nameof(icd10Catalog));
        _patientService = patientService ?? throw new ArgumentNullException(nameof(patientService));
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _medicationCategoryFilter = MedicationCategoryFilters[0];

        SearchMedicationsCommand = new AsyncRelayCommand(SearchMedicationsAsync);
        OrderSelectedMedicationCommand = new AsyncRelayCommand(OrderSelectedMedicationAsync, () => !_board.IsBusy);
        RecordAdministrationCommand = new AsyncRelayCommand(RecordAdministrationAsync, () => !_board.IsBusy);
        SearchBundlesCommand = new AsyncRelayCommand(SearchBundlesAsync);
        ApplyBundleCommand = new AsyncRelayCommand(ApplyBundleAsync, () => !_board.IsBusy);
        SearchDiagnosesCommand = new AsyncRelayCommand(SearchDiagnosesAsync);
        AssignDiagnosisCommand = new AsyncRelayCommand(AssignDiagnosisAsync, () => !_board.IsBusy);
    }

    /// <summary>Preparaty z formularza oddziału (pełny formularz lub wynik wyszukiwania).</summary>
    public ObservableCollection<MedicationDto> Medications { get; } = new();

    /// <summary>Pakiety medyczne dostępne w oddziale.</summary>
    public ObservableCollection<MedicalBundleDto> Bundles { get; } = new();

    /// <summary>Rozpoznania ICD-10 (wynik wyszukiwania lub lista typowa dla SOR).</summary>
    public ObservableCollection<Icd10CatalogEntryDto> Diagnoses { get; } = new();

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

    public string BundleTerm
    {
        get => _bundleTerm;
        set => SetProperty(ref _bundleTerm, value);
    }

    public MedicalBundleDto? SelectedBundle
    {
        get => _selectedBundle;
        set => SetProperty(ref _selectedBundle, value);
    }

    public string Icd10Term
    {
        get => _icd10Term;
        set => SetProperty(ref _icd10Term, value);
    }

    public Icd10CatalogEntryDto? SelectedDiagnosis
    {
        get => _selectedDiagnosis;
        set
        {
            if (SetProperty(ref _selectedDiagnosis, value) && value is not null)
            {
                Icd10Code = value.Code;
            }
        }
    }

    /// <summary>Kod rozpoznania przekazywany do karty pacjenta (może być wpisany ręcznie).</summary>
    public string Icd10Code
    {
        get => _icd10Code;
        set => SetProperty(ref _icd10Code, value);
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

    public AsyncRelayCommand SearchBundlesCommand { get; }

    public AsyncRelayCommand ApplyBundleCommand { get; }

    public AsyncRelayCommand SearchDiagnosesCommand { get; }

    public AsyncRelayCommand AssignDiagnosisCommand { get; }

    /// <summary>Wczytuje formularz, pakiety i typowe rozpoznania — wywoływane po zalogowaniu.</summary>
    public async Task InitializeAsync()
    {
        await SearchMedicationsAsync().ConfigureAwait(true);
        await SearchBundlesAsync().ConfigureAwait(true);
        await SearchDiagnosesAsync().ConfigureAwait(true);
    }

    /// <summary>Czyści formularz po zakończeniu sesji — nie zostawia danych poprzedniego użytkownika.</summary>
    public void Clear()
    {
        Medications.Clear();
        Bundles.Clear();
        Diagnoses.Clear();
        SelectedMedication = null;
        SelectedBundle = null;
        SelectedDiagnosis = null;
        Icd10Code = string.Empty;
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

    private async Task SearchBundlesAsync()
    {
        try
        {
            var result = await _bundleService.SearchAsync(BundleTerm, CancellationToken.None).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                Replace(Bundles, result);
                StatusMessage = $"Dostępne pakiety medyczne: {result.Count}.";
            }).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task ApplyBundleAsync()
    {
        if (_selectedBundle is null)
        {
            StatusMessage = "Wybierz pakiet medyczny.";
            return;
        }

        if (_board.SelectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta na pulpicie, aby zastosować pakiet.";
            return;
        }

        try
        {
            var patient = await _bundleService
                .ApplyAsync(_board.SelectedPatient.Id, _selectedBundle.Id, itemIds: null, CancellationToken.None)
                .ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() => _board.ShowDetails(patient)).ConfigureAwait(true);

            StatusMessage =
                $"Zastosowano pakiet „{_selectedBundle.Name}” ({_selectedBundle.OrderCount} zleceń).";

            await _board.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task SearchDiagnosesAsync()
    {
        try
        {
            var result = string.IsNullOrWhiteSpace(Icd10Term)
                ? await _icd10Catalog.GetEmergencyRelevantAsync(CancellationToken.None).ConfigureAwait(true)
                : await _icd10Catalog.SearchAsync(Icd10Term, CancellationToken.None).ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() =>
            {
                Replace(Diagnoses, result);
                StatusMessage = string.IsNullOrWhiteSpace(Icd10Term)
                    ? $"Rozpoznania typowe dla SOR: {result.Count}."
                    : $"Znaleziono rozpoznań: {result.Count}.";
            }).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = ExceptionMessageMapper.Map(exception).Message;
        }
    }

    private async Task AssignDiagnosisAsync()
    {
        if (_board.SelectedPatient is null)
        {
            StatusMessage = "Wybierz pacjenta na pulpicie, aby zapisać rozpoznanie.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Icd10Code))
        {
            StatusMessage = "Wybierz rozpoznanie z katalogu lub wpisz kod ICD-10.";
            return;
        }

        try
        {
            var patient = await _icd10Catalog
                .AssignAsync(_board.SelectedPatient.Id, Icd10Code.Trim(), CancellationToken.None)
                .ConfigureAwait(true);

            await _dispatcher.InvokeAsync(() => _board.ShowDetails(patient)).ConfigureAwait(true);

            StatusMessage = "Zapisano rozpoznanie ICD-10.";
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


