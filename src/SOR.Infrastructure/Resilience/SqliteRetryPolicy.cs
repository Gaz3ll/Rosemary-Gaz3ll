using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SOR.Infrastructure.Resilience;

/// <summary>Kontrakt polityki ponawiania operacji wrażliwych na blokady pliku bazy.</summary>
public interface IRetryPolicy
{
    Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);

    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Polityka ponawiania z wykładniczym backoffem dla SQLite.
/// Obsługuje blokadę bazy (SQLITE_BUSY / SQLITE_LOCKED) oraz przejściowe błędy I/O,
/// które są typowe dla plikowej bazy danych używanej na stacjach roboczych SOR.
/// </summary>
public sealed class SqliteRetryPolicy : IRetryPolicy
{
    private const int MaxAttempts = 4;
    private const int BaseDelayMs = 50;

    private static readonly int[] RetryableSqliteErrorCodes = { 5, 6 }; // SQLITE_BUSY, SQLITE_LOCKED

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var attempt = 0;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                var delay = TimeSpan.FromMilliseconds(BaseDelayMs * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteAsync<object?>(async ct =>
        {
            await operation(ct).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rozpoznaje wyjątki, które warto ponowić.</summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        SqliteException sqliteEx => RetryableSqliteErrorCodes.Contains(sqliteEx.SqliteErrorCode),
        DbUpdateConcurrencyException => false, // konflikt logiki — nie ponawiamy automatycznie
        IOException => true,
        _ => false
    };
}

/// <summary>Wyjątek sygnalizujący konflikt współbieżnej modyfikacji (optimistic concurrency).</summary>
public sealed class ConcurrencyConflictException : Application.Interfaces.SorApplicationException
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException, "SOR-APP-022")
    {
    }
}