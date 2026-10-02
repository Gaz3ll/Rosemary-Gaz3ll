using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SOR.Application.Interfaces;
using SOR.Domain.Common;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Inicjalizator trwałego magazynu danych: tworzy schemat bazy SQLite, wykrywa schemat
/// nieaktualny po rozbudowie modelu domenowego oraz raportuje błędy wejścia-wyjścia
/// w postaci kontrolowanych wyjątków.
///
/// <para>
/// <c>EnsureCreated</c> z definicji jest bezczynny, gdy plik bazy już istnieje — nie potrafi
/// dodać tabel pojawiających się w nowszej wersji modelu. Bez poniższej kontroli aplikacja
/// uruchomiłaby się bez błędu, a pierwsze zapytanie do nowej tabele zakończyłoby się
/// wyjątkiem „no such table". Dlatego przed utworzeniem schematu porównujemy listę tabel
/// z modelem i — jeśli brakuje którejkolwiek — przebudowujemy schemat od zera.
/// </para>
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly SorDbContext _context;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly string _databasePath;

    public DatabaseInitializer(SorDbContext context, ILogger<DatabaseInitializer> logger, DatabaseConnectionInfo connectionInfo)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(connectionInfo);
        _databasePath = connectionInfo.DatabasePath;
    }

    /// <summary>
    /// Tworzy schemat bazy, jeśli jeszcze nie istnieje, a w razie niezgodności schematu
    /// z bieżącym modelem domenowym odtwarza go. Odporny na blokady pliku.
    /// </summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_databasePath))
            {
                var missingTables = await FindMissingTablesAsync(cancellationToken).ConfigureAwait(false);

                if (missingTables.Count > 0)
                {
                    _logger.LogWarning(
                        "Schemat bazy jest nieaktualny — brakuje tabel: {Tables}. Odtwarzam schemat; dane demonstracyjne zostaną wygenerowane ponownie.",
                        string.Join(", ", missingTables));

                    await RecreateSchemaAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await _context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Schemat bazy danych SOR gotowy: {Path}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Nie udało się zainicjalizować bazy danych: {Path}", _databasePath);
            throw new PersistenceException(
                $"Nie udało się otworzyć lub utworzyć pliku bazy danych '{_databasePath}'. " +
                "Sprawdź uprawnienia do katalogu lub czy plik nie jest otwarty przez inną instancję aplikacji.",
                ex);
        }
    }

    /// <summary>Zwraca nazwy tabel zdefiniowanych w modelu, których brakuje w pliku bazy.</summary>
    private async Task<IReadOnlyList<string>> FindMissingTablesAsync(CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existing.Add(reader.GetString(0));
            }
        }

        return _context.Model
            .GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => !string.IsNullOrWhiteSpace(tableName) && !existing.Contains(tableName!))
            .Select(tableName => tableName!)
            .ToList();
    }

    /// <summary>
    /// Usuwa dotychczasowe tabele i pozwala <c>EnsureCreated</c> odtworzyć schemat
    /// zgodny z bieżącym modelem. Dane zostają odtworzone przez <c>DatabaseSeeder</c>.
    /// </summary>
    private async Task RecreateSchemaAsync(CancellationToken cancellationToken)
    {
        var existingTables = await ReadExistingTablesAsync(cancellationToken).ConfigureAwait(false);

        // Wyłączenie kontroli kluczy obcych na czas upuszczania tabel — inaczej kolejność
        // usuwania musiałaby respektować graf powiązań.
        await ExecuteNonQueryAsync("PRAGMA foreign_keys = OFF;", cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var table in existingTables)
            {
                // Identyfikatory pochodzą z katalogu SQLite, jednak zawijanie w cudzysłowy
                // zabezpiecza przed znakami cudzysłowu w nazwach.
                await ExecuteNonQueryAsync(
                    $"DROP TABLE IF EXISTS \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        }

        _context.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<string>> ReadExistingTablesAsync(CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        var connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = CreateCommand(connection, "SELECT name FROM sqlite_master WHERE type = 'table';");

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = CreateCommand(connection, sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
