namespace SOR.Domain.Common;

/// <summary>
/// Abstrakcyjny zegar domenowy. Wstrzykiwany zamiast <see cref="DateTimeOffset.UtcNow"/>,
/// aby testy jednostkowe mogły sterować upływem czasu (rotacja zmian, limity Triage, zboczenia).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}

/// <summary>Domyślna implementacja zegara opartej na zegarze systemowym.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}