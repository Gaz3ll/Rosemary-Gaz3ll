using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SOR.Application.Interfaces;
using SOR.Infrastructure.Persistence.Repositories;
using SOR.Infrastructure.Resilience;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Implementacja wzorca Unit of Work dla SQLite. Zatwierdzenie zmian jest transakcyjne
/// (wzrost wydajności: jedna transakcja na całą operację biznesową) i odporne na blokady pliku bazy
/// dzięki automatycznemu ponawianiu z wykładniczym backoffem.
/// </summary>
public sealed class SqliteUnitOfWork : IUnitOfWork
{
    private readonly SorDbContext _context;
    private readonly IRetryPolicy _retryPolicy;

    private readonly IPatientRepository _patients;
    private readonly IZoneRepository _zones;
    private readonly IUserRepository _users;
    private readonly IStaffZoneAssignmentRepository _staffAssignments;
    private readonly IDutyShiftRepository _dutyShifts;
    private readonly IMedicalOrderRepository _medicalOrders;
    private readonly IAuditLogRepository _auditEntries;
    private readonly ITriageAssessmentRepository _triageAssessments;
    private readonly IZoneTransferRepository _zoneTransfers;

    private IDbContextTransaction? _currentTransaction;
    private bool _disposed;

    public SqliteUnitOfWork(SorDbContext context, IRetryPolicy retryPolicy)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));

        // Repozytoria tworzone raz i współdzielone — zachowują tożsamość śledzenia zmian.
        _patients = new PatientRepository(_context);
        _zones = new ZoneRepository(_context);
        _users = new UserRepository(_context);
        _staffAssignments = new StaffZoneAssignmentRepository(_context);
        _dutyShifts = new DutyShiftRepository(_context);
        _medicalOrders = new MedicalOrderRepository(_context);
        _auditEntries = new AuditLogRepository(_context);
        _triageAssessments = new TriageAssessmentRepository(_context);
        _zoneTransfers = new ZoneTransferRepository(_context);
    }

    public IPatientRepository Patients => _patients;

    public IZoneRepository Zones => _zones;

    public IUserRepository Users => _users;

    public IStaffZoneAssignmentRepository StaffAssignments => _staffAssignments;

    public IDutyShiftRepository DutyShifts => _dutyShifts;

    public IMedicalOrderRepository MedicalOrders => _medicalOrders;

    public IAuditLogRepository AuditEntries => _auditEntries;

    public ITriageAssessmentRepository TriageAssessments => _triageAssessments;

    public IZoneTransferRepository ZoneTransfers => _zoneTransfers;

    public bool HasPendingChanges => _context.ChangeTracker.HasChanges();

    /// <summary>Zapisuje zmiany w pojedynczej transakcji, z obsługą blokad pliku bazy.</summary>
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        _retryPolicy.ExecuteAsync(
            async ct =>
            {
                if (_currentTransaction is not null)
                {
                    // Transakcja jawna jest już otwarta — zapisujemy w jej ramach.
                    return await _context.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                await using var transaction = await _context.Database
                    .BeginTransactionAsync(ct).ConfigureAwait(false);

                var result = await _context.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            },
            cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            _currentTransaction.Rollback();
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }

        _context.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Rozpoczyna transakcję obejmującą wiele operacji (np. rotacja personelu + audyt).</summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            _currentTransaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await _currentTransaction.DisposeAsync().ConfigureAwait(false);
            _currentTransaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync().ConfigureAwait(false);
        }

        await _context.DisposeAsync().ConfigureAwait(false);
    }
}