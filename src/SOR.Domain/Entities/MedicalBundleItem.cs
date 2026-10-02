using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Pozycja pakietu medycznego — pojedynczy krok postępowania.
///
/// <para>
/// Pozycja może wskazywać lek katalogowy (wtedy po zastosowaniu pakietu powstaje zarówno
/// zlecenie typu <see cref="MedicalOrderType.Medication"/>, jak i wpis w rejestrze podanych
/// leków) albo być krokiem niefarmakologicznym — badaniem, zabiegiem lub obserwacją.
/// </para>
/// </summary>
public sealed class MedicalBundleItem : Entity<Guid>
{
    private MedicalBundleItem() { }

    private MedicalBundleItem(
        Guid id,
        Guid bundleId,
        int sequence,
        MedicalOrderType orderType,
        string description,
        MedicationRoute? route,
        Guid? medicationId,
        string? dose,
        bool isUrgent)
    {
        Id = id;
        BundleId = bundleId;
        Sequence = sequence;
        OrderType = orderType;
        Description = description;
        Route = route;
        MedicationId = medicationId;
        Dose = dose;
        IsUrgent = isUrgent;
    }

    public Guid BundleId { get; private set; }

    /// <summary>Kolejność realizacji w pakiecie (1 = pierwszy).</summary>
    public int Sequence { get; private set; }

    public MedicalOrderType OrderType { get; private set; }

    /// <summary>Opis kroku — trafia do zlecenia lekarskiego po zastosowaniu pakietu.</summary>
    public string Description { get; private set; }

    /// <summary>Droga podania dla pozycji farmakologicznych.</summary>
    public MedicationRoute? Route { get; private set; }

    /// <summary>Preparat z katalogu, jeżeli pozycja dotyczy podania leku.</summary>
    public Guid? MedicationId { get; private set; }

    /// <summary>Dawka zgodna z pozycją katalogu leku.</summary>
    public string? Dose { get; private set; }

    public bool IsUrgent { get; private set; }

    /// <summary>Czy pozycja wymaga zlecenia lekarskego, czy tylko odnotowania podania.</summary>
    public bool RequiresOrder => OrderType != MedicalOrderType.Observation;

    internal static MedicalBundleItem Create(
        Guid id,
        Guid bundleId,
        int sequence,
        MedicalOrderType orderType,
        string description,
        MedicationRoute? route,
        Guid? medicationId,
        string? dose,
        bool isUrgent)
    {
        if (id == Guid.Empty || bundleId == Guid.Empty)
        {
            throw new ValidationException("Identyfikatory pozycji i pakietu są wymagane.", nameof(id));
        }

        if (sequence < 1)
        {
            throw new ValidationException("Numer kolejności pozycji musi być dodatni.", nameof(sequence));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ValidationException("Opis pozycji pakietu jest wymagany.", nameof(description));
        }

        // Lek musi mieć przypisaną drogę podania i dawkę — inaczej zlecenie byłoby niejednoznaczne.
        if (orderType == MedicalOrderType.Medication && (route is null || string.IsNullOrWhiteSpace(dose)))
        {
            throw new ValidationException(
                "Pozycja pakietu z lekiem musi określać drogę podania i dawkę.", nameof(orderType));
        }

        if (orderType != MedicalOrderType.Medication && medicationId is not null)
        {
            throw new ValidationException(
                "Preparat można wskazać wyłącznie dla pozycji typu „Podanie leku”.", nameof(medicationId));
        }

        return new MedicalBundleItem(
            id,
            bundleId,
            sequence,
            orderType,
            description.Trim(),
            route,
            medicationId,
            string.IsNullOrWhiteSpace(dose) ? null : dose.Trim(),
            isUrgent);
    }

    public override string ToString() => $"{Sequence}. {Description}";
}
