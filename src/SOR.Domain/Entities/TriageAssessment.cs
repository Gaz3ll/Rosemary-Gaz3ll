using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Domain.Entities;

/// <summary>
/// Encja <c>TriageAssessment</c> — pojedyncza ocena segregacji medycznej.
/// Encja (a nie osobny agregat) należąca do karty pacjenta; historia ocen jest chronologiczna.
/// </summary>
public sealed class TriageAssessment : Entity<Guid>
{
    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private TriageAssessment() { }
    private TriageAssessment(
        Guid id,
        Guid patientId,
        Guid assessedByUserId,
        TriageCategory category,
        string clinicalJustification,
        string vitalSignsSummary,
        DateTimeOffset assessedAtUtc)
    {
        Id = id;
        PatientId = patientId;
        AssessedByUserId = assessedByUserId;
        Category = category;
        ClinicalJustification = clinicalJustification;
        VitalSignsSummary = vitalSignsSummary;
        AssessedAtUtc = assessedAtUtc;
    }

    public Guid PatientId { get; private set; }

    /// <summary>Użytkownik wykonujący ocenę (lekarz lub pielęgniarka/ratownik).</summary>
    public Guid AssessedByUserId { get; private set; }

    /// <summary>Przyznany kod Triage.</summary>
    public TriageCategory Category { get; private set; }

    /// <summary>Uzasadnienie kliniczne — pole wymagane formalnie.</summary>
    public string ClinicalJustification { get; private set; }

    /// <summary>Podsumowanie parametrów życiowych (RR, HR, GCS, SpO2).</summary>
    public string VitalSignsSummary { get; private set; }

    public DateTimeOffset AssessedAtUtc { get; private set; }

    /// <summary>Polityka (maks. czas oczekiwania i waga) wyprowadzona z kategorii.</summary>
    public TriagePolicy Policy => TriagePolicy.For(Category);

    /// <summary>Maksymalny dopuszczalny czas oczekiwania wynikający z kodu Triage.</summary>
    public TimeSpan MaxWaitTime => Policy.MaxWaitTime;

    /// <summary>Waga pacjenta w kalkulatorze obciążenia strefy.</summary>
    public decimal LoadWeight => Policy.LoadWeight;

    /// <summary>Czy pacjent oczekuje dłużej niż dopuszczalne (BR-01b — alert nadgodzinowy).</summary>
    public bool IsWaitingTimeExceeded(DateTimeOffset nowUtc) => Policy.IsWaitingTimeExceeded(AssessedAtUtc, nowUtc);

    /// <summary>Zalecany czas do ponownej oceny — 15 minut lub 1/3 czasu maksymalnego.</summary>
    public bool IsRetriageNeeded(DateTimeOffset nowUtc)
    {
        var retriageInterval = TimeSpan.FromTicks(Math.Min(MaxWaitTime.Ticks / 3, TimeSpan.FromMinutes(15).Ticks));
        return nowUtc - AssessedAtUtc > (retriageInterval > TimeSpan.Zero ? retriageInterval : TimeSpan.FromMinutes(5));
    }

    /// <summary>Fabryka z walidacją kompletności oceny (BR-01).</summary>
    public static TriageAssessment Create(
        Guid id,
        Guid patientId,
        Guid assessedByUserId,
        TriageCategory category,
        string clinicalJustification,
        string vitalSignsSummary,
        DateTimeOffset assessedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Identyfikator oceny Triage jest wymagany.", nameof(id));
        }

        if (patientId == Guid.Empty)
        {
            throw new ValidationException("Identyfikator pacjenta jest wymagany.", nameof(patientId));
        }

        if (assessedByUserId == Guid.Empty)
        {
            throw new ValidationException("Identyfikator osoby oceniającej jest wymagany.", nameof(assessedByUserId));
        }

        if (string.IsNullOrWhiteSpace(clinicalJustification) || clinicalJustification.Trim().Length < 5)
        {
            throw new ValidationException(
                "Uzasadnienie kliniczne jest wymagane i musi mieć co najmniej 5 znaków (BR-01).",
                nameof(clinicalJustification));
        }

        return new TriageAssessment(
            id,
            patientId,
            assessedByUserId,
            category,
            clinicalJustification.Trim(),
            vitalSignsSummary?.Trim() ?? string.Empty,
            assessedAtUtc);
    }

    public override string ToString() => $"Triage {Category} ({Policy.MaxWaitTime.TotalMinutes:0} min) — {AssessedAtUtc:dd.MM HH:mm}";
}
