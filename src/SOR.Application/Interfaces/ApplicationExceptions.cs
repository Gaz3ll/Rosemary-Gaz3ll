using SOR.Domain.Common;

namespace SOR.Application.Interfaces;

/// <summary>
/// Wyjątek warstwy aplikacji — mapa wyjątków domenowych na komunikaty prezentacyjne.
/// Warstwa prezentacji przechwytuje wyłącznie <see cref="SorApplicationException"/> i <see cref="DomainException"/>,
/// dzięki czemu nie musi znać szczegółów domenowych.
/// </summary>
public class SorApplicationException : Exception
{
    public SorApplicationException(string message, string code = "SOR-APP-001")
        : base(message)
    {
        Code = code;
    }

    public SorApplicationException(string message, Exception innerException, string code = "SOR-APP-002")
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Błąd uwierzytelnienia — nieudane logowanie lub brak aktywnej sesji.</summary>
public sealed class AuthenticationException : SorApplicationException
{
    public AuthenticationException(string message, string code = "SOR-APP-010")
        : base(message, code)
    {
    }
}

/// <summary>Brak uprawnień do wykonania operacji w kontekście aktualnej strefy (BR-12/BR-13).</summary>
public sealed class AuthorizationException : SorApplicationException
{
    public AuthorizationException(string message, string code = "SOR-APP-011")
        : base(message, code)
    {
    }
}

/// <summary>Wystąpił błąd wejścia-wyjścia podczas operacji na trwałym magazynie danych.</summary>
public sealed class PersistenceException : SorApplicationException
{
    public PersistenceException(string message, Exception innerException)
        : base(message, innerException, "SOR-APP-020")
    {
    }

    public PersistenceException(string message, string code = "SOR-APP-021")
        : base(message, code)
    {
    }
}

/// <summary>Próba pobrania lub zaktualizowania nieistniejącego zasobu.</summary>
public sealed class EntityNotFoundException : SorApplicationException
{
    public EntityNotFoundException(string entityName, object key)
        : base($"Nie znaleziono encji '{entityName}' o identyfikatorze '{key}'.", "SOR-APP-030")
    {
        EntityName = entityName;
        Key = key;
    }

    public string EntityName { get; }

    public object Key { get; }
}

/// <summary>Operacja naruszałaby limit zdolności przyjęciowej strefy (BR-04).</summary>
public sealed class ZoneCapacityExceededException : SorApplicationException
{
    public ZoneCapacityExceededException(string zoneName, int capacity)
        : base($"Strefa '{zoneName}' nie dysponuje wolnym miejscem (zapotrzebowanie {capacity}).", "SOR-APP-040")
    {
        ZoneName = zoneName;
        Capacity = capacity;
    }

    /// <summary>Nazwa strefy, której dotyczy przekroczenie.</summary>
    public string ZoneName { get; }

    /// <summary>Zdolność przyjęciowa strefy w chwili przekroczenia.</summary>
    public int Capacity { get; }
}