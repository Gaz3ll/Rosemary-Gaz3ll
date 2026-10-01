using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Application.Mapping;
using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Application.Services;

/// <summary>
/// Serwis rotacji personelu. Centralny punkt realizujący reguły BR-05, BR-07 i BR-08:
/// każda zmiana strefy jest walidowana, transakcyjna i audytowana.
/// </summary>
public sealed class StaffRotationService : IStaffRotationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IAuditLogService _auditLog;
    private readonly IAuthenticationService _authenticationService;
    private readonly IZoneLoadQueryService _loadQueryService;
    private readonly IDomainEventPublisher _eventPublisher;

    public StaffRotationService(
        IUnitOfWork unitOfWork,
        IClock clock,
        IIdGenerator idGenerator,
        IAuditLogService auditLog,
        IAuthenticationService authenticationService,
        IZoneLoadQueryService loadQueryService,
        IDomainEventPublisher eventPublisher)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _loadQueryService = loadQueryService ?? throw new ArgumentNullException(nameof(loadQueryService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public RotationRecommendationDto? CurrentRecommendation { get; private set; }

    /// <summary>Manualna zmiana strefy — BR-05: obowiązkowe uzasadnienie, walidacja strefy docelowej.</summary>
    public async Task<ReassignmentResultDto> ReassignZoneAsync(
        ReassignZoneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireAuthenticatedUser();

        // BR-05: lekarz lub pielęgniarka mogą zmienić wyłącznie własną strefę.
        // Zmiana cudzej strefy jest zarezerwowana dla koordynatora (metoda CoordinatorAssignAsync).
        if (actor.Id != command.UserId)
        {
            throw new AuthorizationException(
                "Zmiana strefy dotyczy wyłącznie własnego przypisania. " +
                "Rotację innych pracowników może zlecić koordynator / ordynator (BR-08).");
        }

        var reason = ReassignmentReason.Create(command.ReasonCode, command.ReasonComment, _clock.UtcNow, _clock);

        var result = await ReassignInternalAsync(
            command.UserId,
            command.TargetZoneId,
            ReassignmentKind.ManualReassignment,
            reason,
            actor.Login,
            AuditActionType.ZoneReassignedManually,
            cancellationToken).ConfigureAwait(false);

        await _loadQueryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Przyjęcie rekomendacji systemu — BR-07.</summary>
    public async Task<ReassignmentResultDto> AcceptRecommendationAsync(
        AcceptRotationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = RequireAuthenticatedUser();

        // BR-07: rekomendację może przyjąć wyłącznie wskazany kandydat albo koordynator.
        if (actor.Id != command.UserId && !actor.CanManageAllZones)
        {
            throw new AuthorizationException(
                "Rekomendację rotacji może przyjąć jedynie wskazany pracownik lub koordynator (BR-07).");
        }

        var reason = ReassignmentReason.Create(command.ReasonCode, command.ReasonComment, _clock.UtcNow, _clock);

        var result = await ReassignInternalAsync(
            command.UserId,
            command.TargetZoneId,
            ReassignmentKind.RecommendedRotation,
            reason,
            actor.Login,
            AuditActionType.RotationRecommendationAccepted,
            cancellationToken).ConfigureAwait(false);

        CurrentRecommendation = null;
        return result;
    }

    /// <summary>Rotacja zlecona przez koordynatora — BR-08.</summary>
    public async Task<ReassignmentResultDto> CoordinatorAssignAsync(
        Guid targetUserId,
        Guid targetZoneId,
        ReassignmentReasonCode reasonCode,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();

        if (!actor.CanManageAllZones)
        {
            throw new AuthorizationException(
                "Rotację innych pracowników może zlecić wyłącznie koordynator / ordynator (BR-08).");
        }

        var reason = ReassignmentReason.Create(reasonCode, comment, _clock.UtcNow, _clock);

        return await ReassignInternalAsync(
            targetUserId,
            targetZoneId,
            ReassignmentKind.CoordinatorOrder,
            reason,
            actor.Login,
            AuditActionType.ZoneReassignedManually,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ZoneLoadDto>> GetReassignmentTargetsAsync(CancellationToken cancellationToken = default)
    {
        var actor = RequireAuthenticatedUser();
        var snapshot = await _loadQueryService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        return snapshot.Zones
            .Where(z => z.ZoneId != actor.CurrentZoneId)
            .Where(z => z.Status != Domain.Enums.ZoneLoadStatus.Overloaded || actor.Role == UserRole.Coordinator)
            .OrderBy(z => z.LoadRatio)
            .ToList();
    }

    public async Task<IReadOnlyList<RotationHistoryDto>> GetRotationHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var assignments = await _unitOfWork.StaffAssignments
            .GetHistoryForUserAsync(userId, cancellationToken).ConfigureAwait(false);

        if (assignments.Count == 0)
        {
            return Array.Empty<RotationHistoryDto>();
        }

        var users = await _unitOfWork.Users.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var zones = await _unitOfWork.Zones.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var userNames = users.ToDictionary(u => u.Id, u => u.DisplayName);
        var zoneNames = zones.ToDictionary(z => z.Id, z => z.Name);

        return assignments
            .Select(a =>
            {
                var fromZone = zones.FirstOrDefault(z =>
                    a.SupersedesAssignmentId is not null && z.Id == ZoneIdFromSuperseded(assignments, a));
                var toName = zoneNames.GetValueOrDefault(a.ZoneId, "(brak)");

                return a.ToHistoryDto(
                    userNames.GetValueOrDefault(a.UserId, "(nieznany)"),
                    fromZone?.Name ?? "(poprzednia strefa)",
                    toName);
            })
            .OrderByDescending(r => r.EffectiveFromUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<StaffAssignmentDto>> GetStaffByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        var assignments = await _unitOfWork.StaffAssignments
            .GetActiveByZoneAsync(zoneId, cancellationToken).ConfigureAwait(false);

        if (assignments.Count == 0)
        {
            return Array.Empty<StaffAssignmentDto>();
        }

        var users = await _unitOfWork.Users.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var userById = users.ToDictionary(u => u.Id);
        var zone = await _unitOfWork.Zones.GetByIdAsync(zoneId, cancellationToken).ConfigureAwait(false);

        var activePatientCount = await _unitOfWork.Patients
            .CountActiveByZoneAsync(zoneId, cancellationToken).ConfigureAwait(false);

        var currentUser = _authenticationService.CurrentUser;

        return assignments
            .Where(a => userById.ContainsKey(a.UserId))
            .Select(a =>
            {
                var dto = a.ToDto(userById[a.UserId], zone?.Name ?? "(nieznana)", activePatientCount);
                return dto.IsCurrentUser
                    ? dto
                    : dto with { IsCurrentUser = currentUser?.Id == a.UserId };
            })
            .OrderBy(s => s.DisplayName)
            .ToList();
    }

    /// <summary>Ustawia aktualną rekomendację (wywoływane przez serwis monitoringu).</summary>
    internal void SetRecommendation(RotationRecommendationDto? recommendation) => CurrentRecommendation = recommendation;

    // ---------- Implementacja wspólna ----------

    /// <summary>
    /// Wspólny algorytm zmiany strefy: walidacja, zamknięcie poprzedniego przypisania,
    /// utworzenie nowego, wpis audytowy i zapis transakcyjny.
    /// </summary>
    private async Task<ReassignmentResultDto> ReassignInternalAsync(
        Guid targetUserId,
        Guid targetZoneId,
        ReassignmentKind kind,
        ReassignmentReason reason,
        string actorLogin,
        AuditActionType auditAction,
        CancellationToken cancellationToken)
    {
        var assignments = _unitOfWork.StaffAssignments;
        var users = _unitOfWork.Users;
        var zones = _unitOfWork.Zones;
        var patients = _unitOfWork.Patients;

        var targetUser = await users.GetByIdAsync(targetUserId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(User), targetUserId);

        if (!targetUser.IsActive)
        {
            throw new AuthorizationException($"Użytkownik '{targetUser.Login}' został zdezaktywowany.");
        }

        var targetZone = await zones.GetByIdAsync(targetZoneId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Zone), targetZoneId);

        if (!targetZone.CanAcceptRotation())
        {
            throw new InvalidZoneReassignmentException(
                $"Strefa '{targetZone.Name}' jest nieaktywna i nie przyjmuje personelu.");
        }

        var current = await assignments.GetActiveForUserAsync(targetUserId, cancellationToken).ConfigureAwait(false);

        if (current is not null && current.ZoneId == targetZoneId)
        {
            throw new InvalidZoneReassignmentException(
                $"Użytkownik '{targetUser.Login}' jest już przypisany do strefy '{targetZone.Name}'. " +
                "Zmiana na tę samą strefę jest niedozwolona (BR-05).");
        }

        // BR-04: kontrola zdolności przyjęciowej strefi docelowej.
        var activeInTarget = await patients.CountActiveByZoneAsync(targetZoneId, cancellationToken).ConfigureAwait(false);
        try
        {
            targetZone.EnsureCanAcceptPatient(activeInTarget);
        }
        catch (ValidationException ex)
        {
            await AuditFailureAsync(auditAction, targetUserId, actorLogin, ex.Message, cancellationToken).ConfigureAwait(false);
            throw new ZoneCapacityExceededException(targetZone.Name, targetZone.Capacity);
        }

        var fromZoneName = "(brak przypisania)";
        Guid? supersedesId = null;

        if (current is not null)
        {
            var fromZone = current.ZoneId == Guid.Empty
                ? null
                : await zones.GetByIdAsync(current.ZoneId, cancellationToken).ConfigureAwait(false);

            fromZoneName = fromZone?.Name ?? "(brak przypisania)";
            supersedesId = current.Id;
            current.Close(_clock.UtcNow);
        }

        var newAssignment = CreateAssignment(
            _idGenerator.NewId(),
            targetUserId,
            targetZoneId,
            kind,
            reason,
            supersedesId);

        newAssignment.EnsureZoneSpecified();
        newAssignment.EnsureReasonRequired();

        await assignments.AddAsync(newAssignment, cancellationToken).ConfigureAwait(false);

        var auditDescription =
            $"{newAssignment.DescribeChange()}; {fromZoneName} → {targetZone.Name}; użytkownik: {targetUser.DisplayName}";

        var auditEntry = await _auditLog.RecordAsync(
            auditAction,
            targetUserId,
            actorLogin,
            newAssignment.Id,
            nameof(StaffZoneAssignment),
            auditDescription,
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        _eventPublisher.PublishStaffReassigned(new StaffZoneReassignedEvent
        {
            AssignmentId = newAssignment.Id,
            UserId = targetUserId,
            UserLogin = targetUser.Login,
            FromZoneId = current?.ZoneId ?? Guid.Empty,
            ToZoneId = targetZoneId,
            ReasonCode = reason.Code.ToString()
        });

        // BR-16: jeśli rotacja dotyczy bieżącej sesji, odświeżamy jej kontekst strefowy.
        if (_authenticationService.CurrentUser?.Id == targetUserId)
        {
            await _authenticationService.RefreshCurrentUserContextAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ReassignmentResultDto(
            newAssignment.Id,
            targetUserId,
            targetUser.DisplayName,
            current?.ZoneId ?? Guid.Empty,
            fromZoneName,
            targetZone.Id,
            targetZone.Name,
            kind,
            reason.ToString(),
            newAssignment.EffectiveFromUtc,
            $"Zmiana strefy zapisana w dzienniku audytu (wpis {auditEntry:N}).");
    }

    private StaffZoneAssignment CreateAssignment(
        Guid id,
        Guid userId,
        Guid zoneId,
        ReassignmentKind kind,
        ReassignmentReason reason,
        Guid? supersedesId) => kind switch
    {
        ReassignmentKind.ManualReassignment => StaffZoneAssignment.Manual(id, userId, zoneId, reason, _clock.UtcNow, supersedesId),
        ReassignmentKind.RecommendedRotation => StaffZoneAssignment.Recommended(id, userId, zoneId, reason, _clock.UtcNow, supersedesId),
        ReassignmentKind.CoordinatorOrder => StaffZoneAssignment.OrderedByCoordinator(id, userId, zoneId, reason, _clock.UtcNow, supersedesId),
        _ => throw new InvalidZoneReassignmentException($"Rodzaj przypisania '{kind}' nie obsługuje przyczyny.", nameof(kind))
    };

    private async Task AuditFailureAsync(
        AuditActionType actionType,
        Guid userId,
        string actorLogin,
        string message,
        CancellationToken cancellationToken)
    {
        await _auditLog.RecordAsync(
            actionType,
            userId,
            actorLogin,
            userId,
            nameof(StaffZoneAssignment),
            $"Odrzucono zmianę strefy: {message}",
            false,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private AuthenticatedUserDto RequireAuthenticatedUser() =>
        _authenticationService.CurrentUser
        ?? throw new AuthenticationException("Zmiana strefy wymaga aktywnej sesji użytkownika.");

    private static Guid ZoneIdFromSuperseded(IReadOnlyList<StaffZoneAssignment> assignments, StaffZoneAssignment current) =>
        assignments
            .FirstOrDefault(a => a.Id == current.SupersedesAssignmentId)?.ZoneId
        ?? Guid.Empty;
}