namespace SOR.Domain.Common;

/// <summary>
/// Generator identyfikatorów GUID. Wydzielony interfejs umożliwia deterministyczne testy
/// oraz docelowe podmienienie na generator sekwencyjny (UUIDv7) w przyszłości.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}

/// <summary>Domyślna implementacja generatora oparta na <c>Guid.CreateVersion7()</c>.</summary>
public sealed class GuidIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}