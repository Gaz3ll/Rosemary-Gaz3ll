using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Wpis wypisu pacjenta z SOR (BR-11). Rejestruje sposób zakończenia pobytu, a w przypadku
/// przekazania na inny oddział — również oddział przyjmujący. Wpis jest niezmienny:
/// powtarzna zmiana sposobu wypisu wymaga nowego wpisu, a nie nadpisania historii.
/// </summary>
public sealed class PatientDischarge : Entity<Guid>
{
    private PatientDischarge() { }

    private PatientDischarge(
        Guid id,
        Guid patientId,
        DischargeType type,
        Guid? departmentId,
        string? departmentName,
        string? reason,
        DateTimeOffset dischargedAtUtc,
        Guid recordedByUserId,
        string recordedByLogin)
    {
        Id = id;
        PatientId = patientId;
        Type = type;
        DepartmentId = departmentId;
        DepartmentName = departmentName;
        Reason = reason;
        DischargedAtUtc = dischargedAtUtc;
        RecordedByUserId = recordedByUserId;
        RecordedByLogin = recordedByLogin;
    }

    public Guid PatientId { get; private set; }

    /// <summary>Sposób zakończenia pobytu: zakończenie leczenia, wypis na własne żądanie lub przekazanie.</summary>
    public DischargeType Type { get; private set; }

    /// <summary>Oddział przyjmujący — wypełniony wyłącznie dla przekazania na inny oddział.</summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>
    /// Nazwa oddziału zapisywana wprost we wpisie. Dzięki temu dokument wypisu jest czytelny
    /// także wtedy, gdy katalog oddziałów w przyszłości się zmieni.
    /// </summary>
    public string? DepartmentName { get; private set; }

    /// <summary>Uzasadnienie wypisu — obowiązkowe dla wypisu na własne żądanie i przekazania.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset DischargedAtUtc { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public string RecordedByLogin { get; private set; }

    public static PatientDischarge Create(
        Guid id,
        Guid patientId,
        DischargeType type,
        Guid? departmentId,
        string? departmentName,
        string? reason,
        DateTimeOffset dischargedAtUtc,
        Guid recordedByUserId,
        string recordedByLogin)
    {
        if (id == Guid.Empty || patientId == Guid.Empty || recordedByUserId == Guid.Empty)
        {
            throw new ValidationException(
                "Identyfikatory wpisu wypisu, pacjenta i osoby zapisującej są wymagane.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(recordedByLogin))
        {
            throw new ValidationException("Login osoby zapisującej wypis jest wymagany.", nameof(recordedByLogin));
        }

        if (type == DischargeType.TransferToDepartment && departmentId is null)
        {
            throw new ValidationException(
                "Przekazanie na inny oddział wymaga wskazania oddziału przyjmującego (BR-11).", nameof(departmentId));
        }

        if (type != DischargeType.TransferToDepartment && departmentId is not null)
        {
            throw new ValidationException(
                "Oddział przyjmujący dotyczy wyłącznie przekazania na inny oddział (BR-11).", nameof(departmentId));
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        return new PatientDischarge(
            id,
            patientId,
            type,
            departmentId,
            departmentName,
            normalizedReason,
            dischargedAtUtc,
            recordedByUserId,
            recordedByLogin);
    }
}