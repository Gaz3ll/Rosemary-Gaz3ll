using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Application.Interfaces;

/// <summary>
/// Kontrakt dostępu do danych (wzorzec Repository). Definicja leży w warstwie aplikacji,
/// aby domena nie zależała od technologii trwałości (zgodnie z Dependency Inversion).
/// </summary>
/// <typeparam name="TEntity">Typ encji agregatu.</typeparam>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>Wyszukuje encję po tożsamości.</summary>
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Zwraca wszystkie encje (opcjonalnie filtrowane).</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Dodaje encję do kontekstu (zatwierdzenie następuje w Unit of Work).</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Usuwa encję z kontekstu.</summary>
    void Remove(TEntity entity);

    /// <summary>Masowe pobranie encji po zbiorze tożsamości (wydajne zapytanie IN).</summary>
    Task<IReadOnlyList<TEntity>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium pacjentów z obsługą blokady współbieżnej.</summary>
public interface IPatientRepository : IRepository<Patient>
{
    /// <summary>Pobiera pacjentów przypisanych do strefy (bez zamkniętych).</summary>
    Task<IReadOnlyList<Patient>> GetByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Pobiera pacjentów oczekujących na triage.</summary>
    Task<IReadOnlyList<Patient>> GetAwaitingTriageAsync(CancellationToken cancellationToken = default);

    /// <summary>Liczy pacjentów aktywnych w strefie (BR-04).</summary>
    Task<int> CountActiveByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Wyszukuje pacjenta po PESEL.</summary>
    Task<Patient?> GetByPeselAsync(string pesel, CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium stref.</summary>
public interface IZoneRepository : IRepository<Zone>
{
    /// <summary>Pobiera strefę po kodzie domenowym.</summary>
    Task<Zone?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Pobiera wszystkie aktywne strefy.</summary>
    Task<IReadOnlyList<Zone>> GetActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium użytkowników.</summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>Wyszukuje użytkownika po znormalizowanym loginie.</summary>
    Task<User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium przypisań personelu do stref.</summary>
public interface IStaffZoneAssignmentRepository : IRepository<StaffZoneAssignment>
{
    /// <summary>Aktualne przypisanie użytkownika (null gdy żadne).</summary>
    Task<StaffZoneAssignment?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Aktywne przypisania wszystkich pracowników.</summary>
    Task<IReadOnlyList<StaffZoneAssignment>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Aktywne przypisania dla strefy.</summary>
    Task<IReadOnlyList<StaffZoneAssignment>> GetActiveByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Pełna historia przypisań użytkownika (najnowsze na początku).</summary>
    Task<IReadOnlyList<StaffZoneAssignment>> GetHistoryForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium grafiku dyżurów.</summary>
public interface IDutyShiftRepository : IRepository<DutyShift>
{
    /// <summary>Aktywny dyżur użytkownika w danym momencie — podstawa logowania kontekstowego (BR-16).</summary>
    Task<DutyShift?> GetActiveShiftAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Dyżury w przedziale czasowym (widok grafiku).</summary>
    Task<IReadOnlyList<DutyShift>> GetShiftsInRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium zleceń lekarskich.</summary>
public interface IMedicalOrderRepository : IRepository<MedicalOrder>
{
    /// <summary>Otwarte zlecenia pacjenta (BR-10).</summary>
    Task<IReadOnlyList<MedicalOrder>> GetOpenByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>Repozytorium wpisów dziennika audytu.</summary>
public interface IAuditLogRepository : IRepository<AuditLogEntry>
{
    /// <summary>Ostatnie wpisy.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Wpisy dotyczące użytkownika.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repozytorium ocen segregacji medycznej (Triage).
///
/// Istnieje mimo przynależności <see cref="TriageAssessment"/> do agregatu pacjenta, ponieważ
/// nową ocenę trzeba jawnie dodać do kontekstu trwałości. Podczas wykrywania zmian EF Core
/// traktuje encję odnalezioną w nawigacji, która ma już ustawiony klucz obcy, jako istniejącą —
/// bez jawnego dodania próbowałby wykonać aktualizację nieistniejącego wiersza.
/// </summary>
public interface ITriageAssessmentRepository : IRepository<TriageAssessment>
{
    /// <summary>Historia ocen pacjenta w kolejności chronologicznej.</summary>
    Task<IReadOnlyList<TriageAssessment>> GetByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repozytorium historii przeniesień pacjenta między strefami. Podobnie jak oceny Triage,
/// nowe przeniesienie trzeba jawnie dodać do kontekstu, aby EF Core wykonał INSERT, a nie
/// aktualizację wiersza, którego jeszcze nie ma.
/// </summary>
public interface IZoneTransferRepository : IRepository<ZoneTransfer>
{
    /// <summary>Historia przeniesień pacjenta w kolejności chronologicznej.</summary>
    Task<IReadOnlyList<ZoneTransfer>> GetByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Jednostka pracy (Unit of Work) — granica transakcji. Zatwierdzenie jest atomowe:
/// rotacja personelu i przeniesienie pacjenta zapisywane są w całości albo wcale.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Zapisuje wszystkie zmiany z kontekstu w pojedynczej transakcji.</summary>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Wycofuje wszystkie niezapisane zmiany z kontekstu.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>Czy istnieją niezapisane zmiany.</summary>
    bool HasPendingChanges { get; }

    /// <summary>Repozytorium pacjentów.</summary>
    IPatientRepository Patients { get; }

    /// <summary>Repozytorium stref.</summary>
    IZoneRepository Zones { get; }

    /// <summary>Repozytorium użytkowników.</summary>
    IUserRepository Users { get; }

    /// <summary>Repozytorium przypisań personelu do stref.</summary>
    IStaffZoneAssignmentRepository StaffAssignments { get; }

    /// <summary>Repozytorium dyżurów.</summary>
    IDutyShiftRepository DutyShifts { get; }

    /// <summary>Repozytorium zleceń lekarskich.</summary>
    IMedicalOrderRepository MedicalOrders { get; }

    /// <summary>Repozytorium wpisów audytowych.</summary>
    IAuditLogRepository AuditEntries { get; }

    /// <summary>Repozytorium ocen segregacji medycznej.</summary>
    ITriageAssessmentRepository TriageAssessments { get; }

    /// <summary>Repozytorium historii przeniesień pacjenta.</summary>
    IZoneTransferRepository ZoneTransfers { get; }
}