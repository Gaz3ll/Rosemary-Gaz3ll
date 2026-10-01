using SOR.Application.Interfaces;
using SOR.Application.Mapping;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Application.Services;

/// <summary>
/// Serwis dziennika audytu. Wpisy są tworzone w kontekście bieżącego Unit of Work,
/// dzięki czemu zapis audytowy jest atomowy z operacją biznesową (wymóg BR-25).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;

    public AuditLogService(IUnitOfWork unitOfWork, IClock clock, IIdGenerator idGenerator)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    }

    /// <summary>
    /// Zapisuje wpis audytowy. Zatwierdzenie transakcji pozostaje w gestii wywołującego serwisu,
    /// dzięki czemu wpis audytowy i operacja biznesowa trafiają do bazy atomowo.
    /// </summary>
    public async Task<Guid> RecordAsync(
        AuditActionType actionType,
        Guid? userId,
        string actorLogin,
        Guid? entityId,
        string? entityType,
        string details,
        bool success = true,
        CancellationToken cancellationToken = default)
    {
        var entry = AuditLogEntry.Record(
            _idGenerator.NewId(),
            actionType,
            userId,
            actorLogin,
            entityId,
            entityType,
            details,
            _clock.UtcNow,
            success);

        await _unitOfWork.AuditEntries.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry.Id;
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        var entries = await _unitOfWork.AuditEntries
            .GetRecentAsync(Math.Clamp(take, 1, 1000), cancellationToken).ConfigureAwait(false);

        var users = await _unitOfWork.Users.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var namesByLogin = users.ToDictionary(u => u.Login, u => u.DisplayName, StringComparer.OrdinalIgnoreCase);

        return entries
            .Select(e => e.ToDto(namesByLogin.GetValueOrDefault(e.ActorLogin)))
            .ToList();
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entries = await _unitOfWork.AuditEntries
            .GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var users = await _unitOfWork.Users.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var namesByLogin = users.ToDictionary(u => u.Login, u => u.DisplayName, StringComparer.OrdinalIgnoreCase);

        return entries
            .Select(e => e.ToDto(namesByLogin.GetValueOrDefault(e.ActorLogin)))
            .ToList();
    }
}