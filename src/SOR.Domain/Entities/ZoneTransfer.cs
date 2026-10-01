using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Encja <c>ZoneTransfer</c> — trwały zapis przeniesienia pacjenta między strefami.
/// Umożliwia pełną rekonstrukcję trasy pacjenta w oddziale (BR-19).
/// </summary>
public sealed class ZoneTransfer : Entity<Guid>
{
    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private ZoneTransfer() { }
    private ZoneTransfer(
        Guid id,
        Guid patientId,
        Guid fromZoneId,
        Guid toZoneId,
        Guid initiatedByUserId,
        string reason,
        DateTimeOffset transferredAtUtc)
    {
        Id = id;
        PatientId = patientId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        InitiatedByUserId = initiatedByUserId;
        Reason = reason;
        TransferredAtUtc = transferredAtUtc;
    }

    public Guid PatientId { get; private set; }

    /// <summary>Strefa źródłowa — <see cref="Guid.Empty"/> oznacza pierwsze przypisanie z triage.</summary>
    public Guid FromZoneId { get; private set; }

    public Guid ToZoneId { get; private set; }

    public Guid InitiatedByUserId { get; private set; }

    /// <summary>Powód przeniesienia — pole wymagane (BR-19).</summary>
    public string Reason { get; private set; }

    public DateTimeOffset TransferredAtUtc { get; private set; }

    /// <summary>Czy jest to pierwsze przypisanie pacjenta do strefy (z modułu triage).</summary>
    public bool IsInitialAssignment => FromZoneId == Guid.Empty;

    public static ZoneTransfer Create(
        Guid id,
        Guid patientId,
        Guid fromZoneId,
        Guid toZoneId,
        Guid initiatedByUserId,
        string reason,
        DateTimeOffset transferredAtUtc)
    {
        if (id == Guid.Empty || patientId == Guid.Empty || toZoneId == Guid.Empty)
        {
            throw new ValidationException("Identyfikatory transferu są wymagane.", nameof(id));
        }

        if (fromZoneId == toZoneId)
        {
            throw new ValidationException(
                "Transfer musi zmieniać strefę — strefa źródłowa i docelowa są identyczne (BR-19).",
                nameof(toZoneId));
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
        {
            throw new ValidationException("Uzasadnienie przeniesienia pacjenta jest wymagane (BR-19).", nameof(reason));
        }

        return new ZoneTransfer(id, patientId, fromZoneId, toZoneId, initiatedByUserId, reason.Trim(), transferredAtUtc);
    }

    /// <summary>Fabryka dla pierwszego przypisania pacjenta do strefy po triage.</summary>
    public static ZoneTransfer InitialAssignment(
        Guid id,
        Guid patientId,
        Guid toZoneId,
        Guid initiatedByUserId,
        DateTimeOffset transferredAtUtc,
        string reason = "Przydział zgodnie z segregacją medyczną") =>
        Create(id, patientId, Guid.Empty, toZoneId, initiatedByUserId, reason, transferredAtUtc);

    public override string ToString() =>
        $"Pacjent {PatientId}: {FromZoneId.ToString()[..Math.Min(8, FromZoneId.ToString().Length)]} → {ToZoneId} ({TransferredAtUtc:dd.MM HH:mm})";
}
