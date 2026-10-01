using Microsoft.EntityFrameworkCore;
using SOR.Application.Interfaces;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence.Repositories;

/// <summary>Repozytorium przypisań personelu do stref — obsługa aktywnych wpisów i historii rotacji.</summary>
public sealed class StaffZoneAssignmentRepository
    : EntityFrameworkRepository<StaffZoneAssignment>, IStaffZoneAssignmentRepository
{
    public StaffZoneAssignmentRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<StaffZoneAssignment?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Context.StaffZoneAssignments
            .Include(a => a.Reason)
            .Where(a => a.UserId == userId && a.EffectiveToUtc == null && a.Kind != ReassignmentKind.Release)
            .OrderByDescending(a => a.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StaffZoneAssignment>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
        await Context.StaffZoneAssignments
            .Include(a => a.Reason)
            .Where(a => a.EffectiveToUtc == null && a.Kind != ReassignmentKind.Release)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StaffZoneAssignment>> GetActiveByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        await Context.StaffZoneAssignments
            .Include(a => a.Reason)
            .Where(a => a.ZoneId == zoneId && a.EffectiveToUtc == null && a.Kind != ReassignmentKind.Release)
            .OrderBy(a => a.EffectiveFromUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StaffZoneAssignment>> GetHistoryForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Context.StaffZoneAssignments
            .Include(a => a.Reason)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.EffectiveFromUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium dyżurów — obsługa odczytu kontekstowego grafiku (BR-16).</summary>
public sealed class DutyShiftRepository : EntityFrameworkRepository<DutyShift>, IDutyShiftRepository
{
    public DutyShiftRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<DutyShift?> GetActiveShiftAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        await Context.DutyShifts
            .AsNoTracking()
            .Where(s => s.UserId == userId &&
                        !s.IsCancelled &&
                        s.StartsAtUtc <= nowUtc &&
                        s.EndsAtUtc > nowUtc)
            .OrderByDescending(s => s.StartsAtUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<DutyShift>> GetShiftsInRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        await Context.DutyShifts
            .AsNoTracking()
            .Where(s => s.StartsAtUtc < toUtc && s.EndsAtUtc > fromUtc)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium zleceń lekarskich.</summary>
public sealed class MedicalOrderRepository : EntityFrameworkRepository<MedicalOrder>, IMedicalOrderRepository
{
    public MedicalOrderRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MedicalOrder>> GetOpenByPatientAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        await Context.MedicalOrders
            .AsNoTracking()
            .Where(o => o.PatientId == patientId &&
                        (o.State == MedicalOrderState.Open || o.State == MedicalOrderState.InProgress))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium dziennika audytu.</summary>
public sealed class AuditLogRepository : EntityFrameworkRepository<AuditLogEntry>, IAuditLogRepository
{
    public AuditLogRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int take, CancellationToken cancellationToken = default) =>
        await Context.AuditLogEntries
            .AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AuditLogEntry>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await Context.AuditLogEntries
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium ocen segregacji medycznej — umożliwia jawne dodanie nowej oceny do kontekstu.</summary>
public sealed class TriageAssessmentRepository
    : EntityFrameworkRepository<TriageAssessment>, ITriageAssessmentRepository
{
    public TriageAssessmentRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<TriageAssessment>> GetByPatientAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        await Context.TriageAssessments
            .AsNoTracking()
            .Where(t => t.PatientId == patientId)
            .OrderBy(t => t.AssessedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium historii przeniesień pacjenta — jawne dodanie nowego wpisu wymusza INSERT.</summary>
public sealed class ZoneTransferRepository
    : EntityFrameworkRepository<ZoneTransfer>, IZoneTransferRepository
{
    public ZoneTransferRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ZoneTransfer>> GetByPatientAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        await Context.ZoneTransfers
            .AsNoTracking()
            .Where(t => t.PatientId == patientId)
            .OrderBy(t => t.TransferredAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}
