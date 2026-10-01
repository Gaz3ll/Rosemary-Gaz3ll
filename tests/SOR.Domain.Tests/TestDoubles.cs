using SOR.Domain.Common;

namespace SOR.Domain.Tests;

/// <summary>
/// Deterministyczny zegar testowy — pozwala sterować upływem czasu w testach rotacji,
/// progów obciążenia i czasów oczekiwania pacjentów.
/// </summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset initialTime)
    {
        UtcNow = initialTime;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);

    /// <summary>Przesuwa zegar o zadany interwał.</summary>
    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);

    /// <summary>Ustawia zegar na konkretny moment.</summary>
    public void Set(DateTimeOffset moment) => UtcNow = moment;
}

/// <summary>Deterministyczny generator identyfikatorów — przewidywalne wartości w testach.</summary>
public sealed class SequentialIdGenerator : IIdGenerator
{
    private long _counter;

    public Guid NewId()
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes, ++_counter);
        return new Guid(bytes);
    }
}