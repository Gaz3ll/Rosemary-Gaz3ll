using SOR.Application.DTOs;
using SOR.Domain.Enums;

namespace SOR.Application.Interfaces;

/// <summary>
/// Serwis katalogu leków — odczyt formularza SOR. Katalog jest danymi referencyjnymi,
/// dlatego serwis udostępnia wyłącznie odczyt; decyzję o podaniu podejmuje lekarz
/// w karcie pacjenta (<see cref="IPatientService"/>).
/// </summary>
public interface IMedicationCatalogService
{
    /// <summary>Wyszukuje preparaty po nazwie, kodzie, postaci lub mocy.</summary>
    Task<IReadOnlyList<MedicationDto>> SearchAsync(
        string? term,
        MedicationCategory? category = null,
        CancellationToken cancellationToken = default);

    /// <summary>Pobiera pojedynczy preparat z katalogu.</summary>
    Task<MedicationDto> GetAsync(Guid medicationId, CancellationToken cancellationToken = default);

    /// <summary>Preparaty o podwyższonym ryzyku — wymagają potwierdzenia przed podaniem.</summary>
    Task<IReadOnlyList<MedicationDto>> GetHighAlertAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista grup terapeutycznych występujących w katalogu (do filtrów widoku).</summary>
    Task<IReadOnlyList<MedicationCategoryOptionDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Serwis pakietów medycznych — odczyt definicji i zastosowanie pakietu na karcie pacjenta.</summary>
public interface IMedicalBundleService
{
    /// <summary>Wszystkie pakiety dostępne w oddziale.</summary>
    Task<IReadOnlyList<MedicalBundleDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Wyszukuje pakiety po nazwie, kodzie lub wskazaniu.</summary>
    Task<IReadOnlyList<MedicalBundleDto>> SearchAsync(string? term, CancellationToken cancellationToken = default);

    /// <summary>Pobiera pojedynczy pakiet wraz z krokami.</summary>
    Task<MedicalBundleDto> GetAsync(Guid bundleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy dla pacjenta zlecenia odpowiadające krokom pakietu. Pozycje farmakologiczne
    /// trafiają również do rejestru podanych leków. Gdy <paramref name="itemIds"/> jest null,
    /// realizowane są wszystkie kroki pakietu.
    /// </summary>
    Task<PatientDetailsDto> ApplyAsync(
        Guid patientId,
        Guid bundleId,
        IReadOnlyCollection<Guid>? itemIds = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Serwis katalogu rozpoznań ICD-10.</summary>
public interface IIcd10CatalogService
{
    /// <summary>Wyszukuje rozpoznania po fragmencie kodu lub opisu.</summary>
    Task<IReadOnlyList<Icd10CatalogEntryDto>> SearchAsync(string? term, CancellationToken cancellationToken = default);

    /// <summary>Rozpoznania typowe dla SOR — używane jako domyślna lista w karcie pacjenta.</summary>
    Task<IReadOnlyList<Icd10CatalogEntryDto>> GetEmergencyRelevantAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia rozpoznanie pacjenta, weryfikując kod względem katalogu. Pusta wartość usuwa rozpoznanie.
    /// </summary>
    Task<PatientDetailsDto> AssignAsync(Guid patientId, string? icd10Code, CancellationToken cancellationToken = default);
}

/// <summary>Grupa terapeutyczna wraz z liczbą preparatów — źródło danych dla filtrów katalogu.</summary>
public sealed record MedicationCategoryOptionDto(MedicationCategory Category, int Count);

/// <summary>
/// Serwis katalogu oddziałów szpitalnych. Katalog jest danymi referencyjnimi i służy
/// wyborowi oddziału przyjmującego podczas przekazania pacjenta z SOR (BR-11).
/// </summary>
public interface IDepartmentCatalogService
{
    /// <summary>Wszystkie oddziały uporządkowane alfabetycznie.</summary>
    Task<IReadOnlyList<DepartmentDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
