using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Inicjalizator trwałego magazynu danych: tworzy schemat bazy SQLite, weryfikuje
/// dostępność ścieżki pliku i raportuje błędy wejścia-wyjścia w postaci kontrolowanych wyjątków.
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

    /// <summary>Tworzy schemat bazy, jeśli jeszcze nie istnieje. Odporny na blokady pliku.</summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await _context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Schemat bazy danych SOR gotowy: {Path}", _databasePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Nie udało się zainicjalizować bazy danych: {Path}", _databasePath);
            throw new Application.Interfaces.PersistenceException(
                $"Nie udało się otworzyć lub utworzyć pliku bazy danych '{_databasePath}'. " +
                "Sprawdź uprawnienia do katalogu lub czy plik nie jest otwarty przez inną instancję aplikacji.",
                ex);
        }
    }
}