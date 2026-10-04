using System.Globalization;
using System.Windows.Data;
using SOR.Domain.Enums;

namespace SOR.Presentation.Converters;

/// <summary>
/// Tłumaczy wartości wyliczeń domenowych na polskie etykiety.
///
/// Interfejs medyczny nie może pokazywać nazw technicznych typu <c>Red</c> czy
/// <c>InProgress</c>. Konwerter utrzymuje mapowanie w jednym miejscu, dzięki czemu
/// widoki nie zawierają rozproszonych tabel etykiet, a testy mogą sprawdzić kompletność
/// mapowania dla wszystkich typów wyliczeń używanych w interfejsie.
/// </summary>
public sealed class PolishEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ToPolish(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("Etykiety wyliczeń są tylko do odczytu.");

    /// <summary>Zwraca polską etykietę wartości wyliczenia.</summary>
    public static string ToPolish(object? value) => value switch
    {
        null => string.Empty,
        TriageCategory category => category switch
        {
            TriageCategory.Red => "Czerwony",
            TriageCategory.Orange => "Pomarańczowy",
            TriageCategory.Yellow => "Żółty",
            TriageCategory.Green => "Zielony",
            TriageCategory.Blue => "Niebieski",
            _ => Fallback(value),
        },
        PatientState state => state switch
        {
            PatientState.Registered => "Zarejestrowany",
            PatientState.Triaged => "Zatriageowany",
            PatientState.InTreatment => "W leczeniu",
            PatientState.AwaitingTransport => "Oczekuje na transport",
            PatientState.TransferredOut => "Przekazany na inny oddział",
            PatientState.Closed => "Wypisany z SOR",
            _ => Fallback(value),
        },

        DischargeType discharge => discharge switch
        {
            DischargeType.TreatmentCompleted => "Zakończenie leczenia (wypis do domu)",
            DischargeType.AtPatientRequest => "Wypis na własne żądanie",
            DischargeType.TransferToDepartment => "Przekazanie na inny oddział",
            _ => Fallback(value),
        },
        PatientGender gender => gender switch
        {
            PatientGender.Female => "Kobieta",
            PatientGender.Male => "Mężczyzna",
            PatientGender.Other => "Inna",
            _ => Fallback(value),
        },
        MedicalOrderType type => type switch
        {
            MedicalOrderType.Lab => "Badanie laboratoryjne",
            MedicalOrderType.Imaging => "Badanie obrazowe",
            MedicalOrderType.Procedure => "Zabieg",
            MedicalOrderType.Medication => "Podanie leku",
            MedicalOrderType.Consultation => "Konsultacja",
            MedicalOrderType.Observation => "Obserwacja",
            _ => Fallback(value),
        },
        MedicalOrderState state => state switch
        {
            MedicalOrderState.Open => "Otwarte",
            MedicalOrderState.InProgress => "W realizacji",
            MedicalOrderState.Completed => "Zrealizowane",
            MedicalOrderState.Cancelled => "Anulowane",
            _ => Fallback(value),
        },
        MedicationRoute route => route switch
        {
            MedicationRoute.Oral => "Doustnie",
            MedicationRoute.Intravenous => "Dożylnie",
            MedicationRoute.Intramuscular => "Domięśniowo",
            MedicationRoute.Subcutaneous => "Podskórnie",
            MedicationRoute.Inhalation => "Wewnątrzopłucnowo",
            MedicationRoute.Mucosal => "Na błonę śluzową",
            MedicationRoute.IntravenousBloodProduct => "Donaczowo (preparat krwiopochodny)",
            MedicationRoute.Tube => "Przez sondę",
            _ => Fallback(value),
        },
        MedicationCategory category => category switch
        {
            MedicationCategory.Analgesic => "Leki przeciwbólowe",
            MedicationCategory.Antibiotic => "Antybiotyki",
            MedicationCategory.Resuscitation => "Leki resuscytacyjne",
            MedicationCategory.Antithrombotic => "Leki przeciwkrzepliwe",
            MedicationCategory.Cardiovascular => "Leki kardiologiczne",
            MedicationCategory.Neurologic => "Leki neurologiczne",
            MedicationCategory.Antiepileptic => "Leki przeciwpadaczkowe",
            MedicationCategory.Antihistamine => "Leki przeciwhistaminowe",
            MedicationCategory.Hormonal => "Leki hormonalne",
            MedicationCategory.Metabolic => "Leki metaboliczne",
            MedicationCategory.FluidAndBlood => "Płyny i preparaty krwiopochodne",
            MedicationCategory.Anesthetic => "Leki znieczulające",
            MedicationCategory.LocalAnesthetic => "Leki miejscowe",
            MedicationCategory.Toxicology => "Leki w zatruciach",
            MedicationCategory.Topical => "Preparaty miejscowe",
            _ => Fallback(value),
        },
        MedicationSafety safety => safety switch
        {
            MedicationSafety.Standard => "Standardowy",
            MedicationSafety.ModifiedRelease => "Zmodyfikowane uwalnianie",
            MedicationSafety.HighAlert => "Lek podwyższonego ryzyka",
            MedicationSafety.BloodProduct => "Preparat krwiopochodny",
            MedicationSafety.Antidote => "Antidotum",
            _ => Fallback(value),
        },
        ZoneKind kind => kind switch
        {
            ZoneKind.Triage => "Moduł triage",
            ZoneKind.Emergency => "Część ratunkowa",
            ZoneKind.Internal => "Część internistyczna",
            ZoneKind.Trauma => "Część urazowa",
            _ => Fallback(value),
        },
        UserRole role => role switch
        {
            UserRole.Physician => "Lekarz",
            UserRole.Nurse => "Pielęgniarka",
            UserRole.Coordinator => "Koordynator",
            UserRole.Paramedic => "Ratownik medyczny",
            _ => Fallback(value),
        },
        ReassignmentKind kind => kind switch
        {
            ReassignmentKind.ScheduledFromRoster => "Zmiana z grafiku",
            ReassignmentKind.ManualReassignment => "Zmiana ręczna",
            ReassignmentKind.RecommendedRotation => "Rotacja zalecana",
            ReassignmentKind.CoordinatorOrder => "Decyzja koordynatora",
            ReassignmentKind.Release => "Zwolnienie ze strefy",
            _ => Fallback(value),
        },
        ReassignmentReasonCode reason => reason switch
        {
            ReassignmentReasonCode.ResuscitationSupport => "Wsparcie resuscytacji",
            ReassignmentReasonCode.EmergencySubstitute => "Zmiana w trybie nagłym",
            ReassignmentReasonCode.TraumaSurge => "Napływ urazów",
            ReassignmentReasonCode.InternalSurge => "Napływ zachorowań",
            ReassignmentReasonCode.TriageSupport => "Wsparcie triage",
            ReassignmentReasonCode.ImagingCoordination => "Koordynacja diagnostyki obrazowej",
            ReassignmentReasonCode.RegulatedBreak => "Uregulowana przerwa",
            ReassignmentReasonCode.EquipmentFailure => "Awaria sprzętu",
            ReassignmentReasonCode.Other => "Inny powód",
            _ => Fallback(value),
        },
        ZoneLoadStatus status => status switch
        {
            ZoneLoadStatus.Optimal => "Obciążenie optymalne",
            ZoneLoadStatus.Elevated => "Obciążenie podwyższone",
            ZoneLoadStatus.Warning => "Obciążenie wysokie",
            ZoneLoadStatus.Overloaded => "Przeciążenie strefy",
            _ => Fallback(value),
        },
        _ => Fallback(value),
    };

    /// <summary>
    /// Wyliczenia objęte mapowaniem. Test kompletności tłumaczeń sprawdza, że każdy element
    /// każdego z tych typów ma etykietę różniącą się od nazwy technicznej.
    /// </summary>
    public static IReadOnlyList<Type> TranslatedEnums { get; } =
    [
        typeof(TriageCategory),
        typeof(PatientState),
        typeof(PatientGender),
        typeof(MedicalOrderType),
        typeof(MedicalOrderState),
        typeof(MedicationRoute),
        typeof(MedicationCategory),
        typeof(MedicationSafety),
        typeof(ZoneKind),
        typeof(UserRole),
        typeof(ReassignmentKind),
        typeof(ReassignmentReasonCode),
        typeof(ZoneLoadStatus),
    ];

    /// <summary>
    /// Tekst zastępczy dla wartości spoza mapowania (np. nowy element dodany do wyliczenia).
    /// Zwraca nazwę techniczną, aby widok nigdy nie pozostał pusty.
    /// </summary>
    private static string Fallback(object value) => value.ToString() ?? string.Empty;
}

