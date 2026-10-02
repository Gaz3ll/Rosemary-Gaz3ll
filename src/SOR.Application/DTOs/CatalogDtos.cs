using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>Pozycja katalogu leków wyświetlana w formularzu SOR.</summary>
public sealed record MedicationDto(
    Guid Id,
    string Code,
    string Name,
    string Form,
    string Strength,
    MedicationCategory Category,
    MedicationRoute Route,
    string TypicalDose,
    string MaxDailyDose,
    MedicationSafety Safety,
    bool IsAvailable,
    string? Contraindications,
    string? Notes)
{
    /// <summary>Nazwa gotowa do wyświetlenia na liście wyboru.</summary>
    public string DisplayName => $"{Name} {Strength} — {Form}";

    /// <summary>Czy preparat wymaga weryfikacji dawki i drogi podania (BR-15).</summary>
    public bool RequiresCloseMonitoring => Safety is MedicationSafety.HighAlert or MedicationSafety.BloodProduct;
}

/// <summary>Krok pakietu medycznego.</summary>
public sealed record MedicalBundleItemDto(
    Guid Id,
    int Sequence,
    MedicalOrderType OrderType,
    string Description,
    MedicationRoute? Route,
    Guid? MedicationId,
    string? MedicationName,
    string? Dose,
    bool IsUrgent);

/// <summary>Pakiet medyczny wraz z uporządkowanymi krokami.</summary>
public sealed record MedicalBundleDto(
    Guid Id,
    string Code,
    string Name,
    string Indication,
    string Chapter,
    IReadOnlyList<MedicalBundleItemDto> Items)
{
    /// <summary>Liczba kroków wymagających zlecenia lekarskiego.</summary>
    public int OrderCount => Items.Count(i => i.OrderType != MedicalOrderType.Observation);
}

/// <summary>Rozpoznanie ICD-10 z katalogu.</summary>
public sealed record Icd10CatalogEntryDto(
    Guid Id,
    string Code,
    string Description,
    string Chapter,
    string? Category,
    bool IsEmergencyRelevant)
{
    /// <summary>Tekst gotowy do wyświetlenia: <c>I10 — Nadciśnienie tętnicze</c>.</summary>
    public string DisplayName => $"{Code} — {Description}";
}

/// <summary>Wpis rejestru podanych leków.</summary>
public sealed record MedicationAdministrationDto(
    Guid Id,
    Guid MedicationId,
    string MedicationName,
    string Dose,
    MedicationRoute Route,
    DateTimeOffset AdministeredAtUtc,
    Guid? MedicalOrderId,
    string? Notes);
