using Microsoft.EntityFrameworkCore;
using SOR.Application.Interfaces;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generyczna implementacja wzorca Repository z użyciem EF Core.
/// Domyślna implementacja <see cref="EntityFrameworkRepository{TEntity}"/> pokrywa operacje
/// wspólne wszystkich agregatów; repozytoria specjalizowane dodają zapytania dziedzinowe.
/// </summary>
public class EntityFrameworkRepository<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    protected SorDbContext Context { get; }

    public EntityFrameworkRepository(SorDbContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Context.Set<TEntity>().FindAsync(new object?[] { id }, cancellationToken).ConfigureAwait(false);

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Context.Set<TEntity>().ToListAsync(cancellationToken).ConfigureAwait(false);

    public virtual async Task<IReadOnlyList<TEntity>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<TEntity>();
        }

        return await Context.Set<TEntity>()
            .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await Context.Set<TEntity>().AddAsync(entity, cancellationToken).ConfigureAwait(false);

    public virtual void Remove(TEntity entity) => Context.Set<TEntity>().Remove(entity);
}

/// <summary>Repozytorium pacjentów z zapytaniami dziedzinowymi.</summary>
public sealed class PatientRepository : EntityFrameworkRepository<Patient>, IPatientRepository
{
    public PatientRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Patient>> GetByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        await Context.Patients
            .AsNoTracking()
            .Include(p => p.CurrentTriage)
            .Where(p => p.ZoneId == zoneId &&
                        p.State != PatientState.Closed &&
                        p.State != PatientState.TransferredOut)
            .OrderBy(p => p.ZoneAssignedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Patient>> GetAwaitingTriageAsync(CancellationToken cancellationToken = default) =>
        await Context.Patients
            .AsNoTracking()
            .Include(p => p.CurrentTriage)
            .Where(p => p.State == PatientState.Registered)
            .OrderBy(p => p.RegisteredAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<int> CountActiveByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
        await Context.Patients
            .AsNoTracking()
            .CountAsync(
                p => p.ZoneId == zoneId &&
                     p.State != PatientState.Closed &&
                     p.State != PatientState.TransferredOut,
                cancellationToken).ConfigureAwait(false);

    public async Task<Patient?> GetByPeselAsync(string pesel, CancellationToken cancellationToken = default) =>
        await Context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Pesel == pesel, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Lista pobytów: pacjenci przyjęci w zakresie dat, razem z oceną Triage i wpisami wypisu.
    /// Zakres liczony jest po dacie przyjęcia do SOR, aby „Data od / Data do” obejmowała cały pobyt.
    /// </summary>
    public async Task<IReadOnlyList<Patient>> GetStaysAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default) =>
        await Context.Patients
            .AsNoTracking()
            .Include(p => p.CurrentTriage)
            .Include(p => p.Discharges)
            .Include(p => p.Orders)
            .Where(p => p.RegisteredAtUtc >= fromUtc && p.RegisteredAtUtc <= toUtc)
            .OrderByDescending(p => p.RegisteredAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public override async Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Context.Patients
            .Include(p => p.CurrentTriage)
            .Include(p => p.Orders)
            .Include(p => p.Transfers)
            .Include(p => p.Administrations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium stref.</summary>
public sealed class ZoneRepository : EntityFrameworkRepository<Zone>, IZoneRepository
{
    public ZoneRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<Zone?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await Context.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(z => z.Code == code.ToUpperInvariant(), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Zone>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await Context.Zones
            .AsNoTracking()
            .Where(z => z.IsActive)
            .OrderBy(z => z.Kind)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Repozytorium użytkowników.</summary>
public sealed class UserRepository : EntityFrameworkRepository<User>, IUserRepository
{
    public UserRepository(SorDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default) =>
        await Context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Login == login.ToLowerInvariant(), cancellationToken).ConfigureAwait(false);

    public override async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
}
