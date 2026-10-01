using SOR.Domain.Common;
using SOR.Domain.Enums;

namespace SOR.Domain.Entities;

/// <summary>
/// Encja <c>AuditLogEntry</c> — niezmienny wpis dziennika audytu. Encja nie udostępnia metod
/// mutacji poza konstrukcją (append-only), co jest wymogiem formalnym ścieżki audytowej.
/// </summary>
public sealed class AuditLogEntry : Entity<Guid>
{
    /// <summary>Konstruktor wywoływany wyłącznie przez warstwę trwałości (EF Core) przy materializacji.</summary>
    private AuditLogEntry() { }
    private AuditLogEntry(
        Guid id,
        AuditActionType actionType,
        Guid? userId,
        string actorLogin,
        Guid? entityId,
        string? entityType,
        string details,
        DateTimeOffset occurredAtUtc,
        bool success)
    {
        Id = id;
        ActionType = actionType;
        UserId = userId;
        ActorLogin = actorLogin;
        EntityId = entityId;
        EntityType = entityType;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
        Success = success;
    }

    public AuditActionType ActionType { get; private set; }

    /// <summary>Identyfikator użytkownika (null dla zdarzeń systemowych / nieudanych prób logowania).</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Login aktora — zapisywany nawet przy nieudanej autoryzacji.</summary>
    public string ActorLogin { get; private set; }

    /// <summary>Identyfikator obiektu domenowego będącego przedmiotem zdarzenia.</summary>
    public Guid? EntityId { get; private set; }

    public string? EntityType { get; private set; }

    /// <summary>Szczegółowy opis zdarzenia — treść niepoddana zmianie po zapisie.</summary>
    public string Details { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Wynik operacji (istotne dla nieudanych logowań i odrzuconych zmian strefy).</summary>
    public bool Success { get; private set; }

    /// <summary>Wersja schematu / znacznik zgodności definicji wpisu audytowego.</summary>
    public int SchemaVersion { get; private set; } = 1;

    public static AuditLogEntry Record(
        Guid id,
        AuditActionType actionType,
        Guid? userId,
        string actorLogin,
        Guid? entityId,
        string? entityType,
        string details,
        DateTimeOffset occurredAtUtc,
        bool success = true)
    {
        if (string.IsNullOrWhiteSpace(actorLogin))
        {
            throw new ValidationException("Login aktora zdarzenia audytowego jest wymagany.", nameof(actorLogin));
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            throw new ValidationException("Opis zdarzenia audytowego jest wymagany.", nameof(details));
        }

        return new AuditLogEntry(
            id,
            actionType,
            userId,
            actorLogin.Trim(),
            entityId,
            entityType,
            details.Trim(),
            occurredAtUtc,
            success);
    }

    /// <summary>Czy wpis dotyczy operacji nieudanej (do wyróżnienia w interfejsie).</summary>
    public bool IsFailure => !Success;

    public override string ToString() =>
        $"[{OccurredAtUtc:yyyy-MM-dd HH:mm:ss}] {ActionType} by {ActorLogin} ({(Success ? "OK" : "BŁĄD")}): {Details}";
}
