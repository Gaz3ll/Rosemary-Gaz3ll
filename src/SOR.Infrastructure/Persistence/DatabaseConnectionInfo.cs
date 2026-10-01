namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Opis połączenia z trwałym magazynem danych. Obiekt przekazywany do warstwy trwałości,
/// aby uniknąć „magic stringów" i umożliwić diagnostykę ścieżki pliku bazy.
/// </summary>
public sealed record DatabaseConnectionInfo(string DatabasePath)
{
    /// <summary>Czy baza danych znajduje się na nośniku lokalnym (wpływa na tryb blokowania pliku).</summary>
    public bool IsLocalFile => !DatabasePath.Contains("Server=", StringComparison.OrdinalIgnoreCase);
}