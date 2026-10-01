using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.ValueObjects;

/// <summary>
/// Uzasadnienie manualnej zmiany strefy. Obiekt wartości realizujący regułę BR-05:
/// przyczyna jest obligatoryjna, a kod <see cref="ReassignmentReasonCode.Other"/>
/// wymaga dodatkowego opisu tekstowego.
/// </summary>
public sealed class ReassignmentReason : ValueObject
{
    private const int MaxCommentLength = 500;

    private ReassignmentReason(ReassignmentReasonCode code, string comment, DateTimeOffset declaredAtUtc)
    {
        Code = code;
        Comment = comment;
        DeclaredAtUtc = declaredAtUtc;
    }

    public ReassignmentReasonCode Code { get; }

    /// <summary>Dodatkowy opis (wymagany dla kodu <see cref="ReassignmentReasonCode.Other"/>, opcjonalny dla pozostałych).</summary>
    public string Comment { get; }

    public DateTimeOffset DeclaredAtUtc { get; }

    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core owned type).</summary>
    private ReassignmentReason()
    {
        Code = ReassignmentReasonCode.Other;
        Comment = string.Empty;
        DeclaredAtUtc = default;
    }

    /// <summary>Słownik opisów kodów używany przez warstwę prezentacji (ComboBox).</summary>
    private static readonly IReadOnlyDictionary<ReassignmentReasonCode, string> Descriptions = new Dictionary<ReassignmentReasonCode, string>
    {
        [ReassignmentReasonCode.ResuscitationSupport] = "Wsparcie resuscytacji",
        [ReassignmentReasonCode.EmergencySubstitute] = "Zastępstwo nagłe",
        [ReassignmentReasonCode.TraumaSurge] = "Wzrost napływu pacjentów urazowych",
        [ReassignmentReasonCode.InternalSurge] = "Wzrost napływu pacjentów internistycznych",
        [ReassignmentReasonCode.TriageSupport] = "Wsparcie triage",
        [ReassignmentReasonCode.ImagingCoordination] = "Koordynacja badania obrazowego",
        [ReassignmentReasonCode.RegulatedBreak] = "Przerwa regulacyjna",
        [ReassignmentReasonCode.EquipmentFailure] = "Awaria sprzętu",
        [ReassignmentReasonCode.Other] = "Inny powód"
    };

    public static IReadOnlyDictionary<ReassignmentReasonCode, string> AllDescriptions => Descriptions;

    /// <summary>Fabryka walidująca kompletność uzasadnienia.</summary>
    public static ReassignmentReason Create(
        ReassignmentReasonCode code,
        string? comment,
        DateTimeOffset declaredAtUtc,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!Descriptions.ContainsKey(code))
        {
            throw new ValidationException($"Nieznany kod przyczyny zmiany strefy: {code}.", nameof(code));
        }

        var trimmed = (comment ?? string.Empty).Trim();

        if (trimmed.Length > MaxCommentLength)
        {
            throw new ValidationException(
                $"Opis przyczyny przekracza limit {MaxCommentLength} znaków (BR-05).",
                nameof(comment));
        }

        if (code == ReassignmentReasonCode.Other && trimmed.Length < 10)
        {
            throw new ValidationException(
                "Dla przyczyny \u201eInny pow\u00f3d\u201d obowi\u0105zuje opis tekstowy o d\u0142ugo\u015bci co najmniej 10 znak\u00f3w (BR-05).",
                nameof(comment));
        }

        if (code != ReassignmentReasonCode.Other && trimmed.Length == 0)
        {
            // Wymuszenie świadomej deklaracji — użytkownik musi potwierdzić powód.
            trimmed = Descriptions[code];
        }

        return new ReassignmentReason(code, trimmed, declaredAtUtc);
    }

    /// <summary>Czy powód wymagał rozwinięcia opisem (służy do walidacji w UI).</summary>
    public bool RequiresDescription => Code == ReassignmentReasonCode.Other;

    protected override IEnumerable<object?> GetEqualityComponents() => new object?[] { Code, Comment, DeclaredAtUtc };

    public override string ToString() => $"{Descriptions[Code]}{(Comment.Length > 0 ? ": " + Comment : string.Empty)}";
}