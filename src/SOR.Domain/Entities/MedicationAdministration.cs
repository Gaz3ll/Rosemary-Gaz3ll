using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Rejestr podanego leku — fakty kliniczny, nie zlecenie. Odpowiada pytaniu
/// „co faktycznie podano pacjentowi, kto podał i kiedy".
///
/// <para>
/// Odrębność od <see cref="MedicalOrder"/> jest istotna: zlecenie wyraża intencję lekarza,
/// a podanie potwierdza wykonanie. Powiązanie jest opcjonalne, ponieważ lek może zostać
/// podany doraźnie (np. w znieczuleniu) bez wcześniejszego zlecenia pisemnego.
/// </para>
/// </summary>
public sealed class MedicationAdministration : Entity<Guid>
{
    private MedicationAdministration() { }

    private MedicationAdministration(
        Guid id,
        Guid patientId,
        Guid medicationId,
        Guid administeredByUserId,
        string dose,
        MedicationRoute route,
        DateTimeOffset administeredAtUtc,
        Guid? medicalOrderId,
        string? notes)
    {
        Id = id;
        PatientId = patientId;
        MedicationId = medicationId;
        AdministeredByUserId = administeredByUserId;
        Dose = dose;
        Route = route;
        AdministeredAtUtc = administeredAtUtc;
        MedicalOrderId = medicalOrderId;
        Notes = notes;
    }

    public Guid PatientId { get; private set; }

    /// <summary>Preparat z katalogu, który został podany.</summary>
    public Guid MedicationId { get; private set; }

    /// <summary>Użytkownik, który faktycznie podał lek.</summary>
    public Guid AdministeredByUserId { get; private set; }

    /// <summary>Podana dawka zgodnie z zleceniem, np. „500 mg i.v.".</summary>
    public string Dose { get; private set; }

    /// <summary>Droga podania.</summary>
    public MedicationRoute Route { get; private set; }

    public DateTimeOffset AdministeredAtUtc { get; private set; }

    /// <summary>Powiązane zlecenie lekarskie, jeżeli podanie wynikało ze zlecenia.</summary>
    public Guid? MedicalOrderId { get; private set; }

    public string? Notes { get; private set; }

    public static MedicationAdministration Record(
        Guid id,
        Guid patientId,
        Guid medicationId,
        Guid administeredByUserId,
        string dose,
        MedicationRoute route,
        DateTimeOffset administeredAtUtc,
        Guid? medicalOrderId = null,
        string? notes = null)
    {
        if (id == Guid.Empty || patientId == Guid.Empty || medicationId == Guid.Empty || administeredByUserId == Guid.Empty)
        {
            throw new ValidationException(
                "Identyfikatory podania, pacjenta, leku i osoby podającej są wymagane.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(dose))
        {
            throw new ValidationException("Dawka podanego leku jest wymagana.", nameof(dose));
        }

        if (administeredAtUtc == default)
        {
            throw new ValidationException("Data podania leku jest wymagana.", nameof(administeredAtUtc));
        }

        return new MedicationAdministration(
            id,
            patientId,
            medicationId,
            administeredByUserId,
            dose.Trim(),
            route,
            administeredAtUtc,
            medicalOrderId,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    public override string ToString() =>
        $"Podanie leku {MedicationId} w dawce {Dose} o {AdministeredAtUtc:u}";
}
