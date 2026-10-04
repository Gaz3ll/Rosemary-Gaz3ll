using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Mapping;

/// <summary>
/// Warstwa mapowania encji domenowych na DTO-y. Oddzielona od serwisów, aby logika biznesowa
/// nie zawierała kosmetycznych operacji projekcji danych na potrzeby UI.
/// </summary>
public static class EntityMapper
{
    /// <summary>Buduje DTO karty pacjenta na pulpicie.</summary>
    public static PatientCardDto ToCardDto(this Patient patient, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(patient);

        var maxWait = patient.CurrentTriage?.MaxWaitTime ?? TimeSpan.Zero;
        var timeInZone = patient.ZoneAssignedAtUtc is null
            ? TimeSpan.Zero
            : nowUtc - patient.ZoneAssignedAtUtc.Value;

        var exceeded = patient.CurrentTriage is not null &&
                       timeInZone > maxWait &&
                       patient.State is PatientState.InTreatment or PatientState.Triaged;

        return new PatientCardDto(
            patient.Id,
            patient.Pesel,
            patient.FullName,
            CalculateAge(patient.DateOfBirth, nowUtc),
            patient.CurrentTriage?.Category ?? TriageCategory.Green,
            patient.State,
            patient.Complaint,
            timeInZone,
            maxWait,
            exceeded,
            patient.LockedBy is not null,
            patient.LockedBy,
            patient.OpenOrders.Count());
    }

    /// <summary>Buduje pełny DTO karty pacjenta.</summary>
    public static PatientDetailsDto ToDetailsDto(
        this Patient patient,
        string? zoneName,
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<Guid, string>? medicationNames = null)
    {
        ArgumentNullException.ThrowIfNull(patient);

        var administrations = patient.Administrations
            .OrderByDescending(a => a.AdministeredAtUtc)
            .Select(a => a.ToDto(ResolveMedicationName(medicationNames, a.MedicationId)))
            .ToList();

        return new PatientDetailsDto(
            patient.Id,
            patient.Pesel,
            patient.FullName,
            patient.DateOfBirth,
            patient.Gender,
            patient.Complaint,
            patient.CurrentTriage?.Category ?? TriageCategory.Green,
            patient.Diagnosis?.Value,
            patient.State,
            patient.ZoneId,
            zoneName,
            patient.RegisteredAtUtc,
            patient.ZoneAssignedAtUtc,
patient.Orders.Select(o => o.ToDto()).ToList(),
               patient.Transfers.Select(t => t.ToDto(zoneName)).ToList(),
               administrations,
               patient.Discharges.Select(d => d.ToDto()).ToList(),
               IsActiveStay(patient.State)
                   ? patient.GetDischargeBlockers(DischargeType.TreatmentCompleted)
                   : []);
       }

    /// <summary>Czy pacjent nadal przebywa w SOR (nie został wypisany ani przekazany).</summary>
    private static bool IsActiveStay(PatientState state) =>
        state is PatientState.InTreatment or PatientState.Triaged or PatientState.AwaitingTransport;

    /// <summary>Buduje DTO wpisu wypisu pacjenta z SOR.</summary>
    public static PatientDischargeDto ToDto(this PatientDischarge discharge)
    {
        ArgumentNullException.ThrowIfNull(discharge);

        return new PatientDischargeDto(
            discharge.Id,
            discharge.Type,
            discharge.DepartmentName,
            discharge.Reason,
            discharge.DischargedAtUtc,
            discharge.RecordedByLogin);
    }

    /// <summary>Buduje DTO pozycji katalogu oddziałów.</summary>
    public static DepartmentDto ToDto(this Department department)
    {
        ArgumentNullException.ThrowIfNull(department);

        return new DepartmentDto(department.Id, department.Code, department.Name);
    }

    private static string ResolveMedicationName(IReadOnlyDictionary<Guid, string>? medicationNames, Guid medicationId)
    {
        if (medicationNames is not null && medicationNames.TryGetValue(medicationId, out var name))
        {
            return name;
        }

        return "(preparat usunięty z katalogu)";
    }

    public static MedicalOrderDto ToDto(this MedicalOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new MedicalOrderDto(
            order.Id,
            order.Type,
            order.Description,
            order.State,
            order.IsUrgent,
            order.OrderedAtUtc,
            order.CompletedAtUtc,
            order.CancellationReason);
    }

    public static ZoneTransferDto ToDto(this ZoneTransfer transfer, string? currentZoneName)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        return new ZoneTransferDto(
            transfer.Id,
            transfer.IsInitialAssignment ? "Moduł triage" : "(poprzednia strefa)",
            currentZoneName ?? transfer.ToZoneId.ToString(),
            transfer.Reason,
            transfer.TransferredAtUtc,
            transfer.IsInitialAssignment);
    }

    public static ZoneLoadDto ToDto(
        this Zone zone,
        LoadRatio loadRatio,
        bool isCurrentUserZone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(loadRatio);

        var freeBeds = Math.Max(zone.Capacity - loadRatio.ActivePatientCount, 0);

        return new ZoneLoadDto(
            zone.Id,
            zone.Code,
            zone.Name,
            zone.Kind,
            loadRatio.Value,
            loadRatio.Status,
            loadRatio.ActivePatientCount,
            loadRatio.StaffCount,
            zone.Capacity,
            freeBeds,
            isCurrentUserZone,
            loadRatio.Rationale);
    }

    public static StaffAssignmentDto ToDto(
        this StaffZoneAssignment assignment,
        User user,
        string zoneName,
        int activePatientCount)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(user);

        return new StaffAssignmentDto(
            assignment.UserId,
            user.Login,
            user.DisplayName,
            user.Role,
            assignment.ZoneId,
            zoneName,
            assignment.Kind,
            assignment.Reason?.ToString() ?? assignment.DescribeChange(),
            assignment.EffectiveFromUtc,
            activePatientCount,
            false);
    }

    public static RotationHistoryDto ToHistoryDto(
        this StaffZoneAssignment assignment,
        string userDisplayName,
        string fromZoneName,
        string toZoneName)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new RotationHistoryDto(
            assignment.Id,
            assignment.UserId,
            userDisplayName,
            assignment.Kind,
            fromZoneName,
            toZoneName,
            assignment.Reason?.Code.ToString() ?? "-",
            assignment.Reason?.Comment ?? string.Empty,
            assignment.EffectiveFromUtc,
            assignment.EffectiveToUtc,
            assignment.IsActive);
    }

    public static MedicationDto ToDto(this Medication medication)
    {
        ArgumentNullException.ThrowIfNull(medication);

        return new MedicationDto(
            medication.Id,
            medication.Code,
            medication.Name,
            medication.Form,
            medication.Strength,
            medication.Category,
            medication.Route,
            medication.TypicalDose,
            medication.MaxDailyDose,
            medication.Safety,
            medication.IsAvailable,
            medication.Contraindications,
            medication.Notes);
    }

    public static MedicalBundleItemDto ToDto(this MedicalBundleItem item, string? medicationName)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new MedicalBundleItemDto(
            item.Id,
            item.Sequence,
            item.OrderType,
            item.Description,
            item.Route,
            item.MedicationId,
            medicationName,
            item.Dose,
            item.IsUrgent);
    }

    public static MedicalBundleDto ToDto(
        this MedicalBundle bundle,
        IReadOnlyDictionary<Guid, string> medicationNames)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(medicationNames);

        var items = bundle.Items
            .OrderBy(i => i.Sequence)
            .Select(i => i.ToDto(
                i.MedicationId is not null && medicationNames.TryGetValue(i.MedicationId.Value, out var name)
                    ? name
                    : null))
            .ToList();

        return new MedicalBundleDto(bundle.Id, bundle.Code, bundle.Name, bundle.Indication, bundle.Chapter, items);
    }

    public static Icd10CatalogEntryDto ToDto(this Icd10CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new Icd10CatalogEntryDto(
            entry.Id,
            entry.Code,
            entry.Description,
            entry.Chapter,
            entry.Category,
            entry.IsEmergencyRelevant);
    }

    public static MedicationAdministrationDto ToDto(this MedicationAdministration administration, string medicationName)
    {
        ArgumentNullException.ThrowIfNull(administration);

        return new MedicationAdministrationDto(
            administration.Id,
            administration.MedicationId,
            medicationName,
            administration.Dose,
            administration.Route,
            administration.AdministeredAtUtc,
            administration.MedicalOrderId,
            administration.Notes);
    }

    public static AuditLogEntryDto ToDto(this AuditLogEntry entry, string? actorDisplayName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditLogEntryDto(
            entry.Id,
            entry.ActionType,
            entry.ActorLogin,
            actorDisplayName,
            entry.Details,
            entry.OccurredAtUtc,
            entry.Success);
    }

    private static int CalculateAge(DateOnly dateOfBirth, DateTimeOffset nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var age = today.Year - dateOfBirth.Year;
        return dateOfBirth > today.AddYears(-age) ? age - 1 : age;
    }
}