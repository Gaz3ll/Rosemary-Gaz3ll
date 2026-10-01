using System.Security.Cryptography;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Application.Services;

/// <summary>
/// Serwis uwierzytelniania z kontekstowym logowaniem. Po poprawnym uwierzytelnieniu
/// odczytuje aktywny dyżur z grafiku, wyznacza przypisanie strefy i ustawia kontekst uprawnień (BR-16).
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private const int MaxFailedAttempts = 3;
    private const int LockoutSeconds = 60;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IAuditLogService _auditLog;
    private readonly IZoneLoadMonitoringService _loadMonitoringService;

    private readonly Dictionary<string, LoginAttemptTracker> _attemptTrackers = new(StringComparer.OrdinalIgnoreCase);

    public AuthenticationService(
        IUnitOfWork unitOfWork,
        IClock clock,
        IIdGenerator idGenerator,
        IAuditLogService auditLog,
        IZoneLoadMonitoringService loadMonitoringService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _loadMonitoringService = loadMonitoringService ?? throw new ArgumentNullException(nameof(loadMonitoringService));
    }

    public event EventHandler<AuthenticatedUserDto?>? SessionChanged;

    public AuthenticatedUserDto? CurrentUser { get; private set; }

    public async Task<AuthenticationSuccessDto> LoginAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            throw new AuthenticationException("Login i hasło są wymagane.", "SOR-APP-011");
        }

        var normalizedLogin = login.Trim().ToLowerInvariant();

        if (IsLockedOut(normalizedLogin))
        {
            throw await RecordFailureAsync(
                normalizedLogin,
                "Konto czasowo zablokowane po serii nieudanych prób logowania.",
                "SOR-APP-012",
                cancellationToken).ConfigureAwait(false);
        }

        var user = await _unitOfWork.Users
            .GetByLoginAsync(normalizedLogin, cancellationToken).ConfigureAwait(false);

        // Stały koszt weryfikacji hasła — brak wycieku informacji o istnieniu konta.
        if (user is null)
        {
            PerformDummyHash(password);
            throw await RecordFailureAsync(normalizedLogin, "Nieprawidłowy login lub hasło.", "SOR-APP-013", cancellationToken)
                .ConfigureAwait(false);
        }

        if (!user.VerifyPassword(password))
        {
            throw await RecordFailureAsync(normalizedLogin, "Nieprawidłowy login lub hasło.", "SOR-APP-013", cancellationToken)
                .ConfigureAwait(false);
        }

        if (!user.IsActive)
        {
            throw await RecordFailureAsync(normalizedLogin, "Konto użytkownika zostało zdezaktywowane.", "SOR-APP-014", cancellationToken)
                .ConfigureAwait(false);
        }

        _attemptTrackers.Remove(normalizedLogin);

        var now = _clock.UtcNow;
        user.RegisterLogin(now);

        var (zoneId, zoneName, zoneKind, shiftId, contextSource, message) =
            await ResolveZoneContextAsync(user, now, cancellationToken).ConfigureAwait(false);

        CurrentUser = new AuthenticatedUserDto(
            user.Id,
            user.Login,
            user.DisplayName,
            user.Role,
            zoneId,
            zoneName,
            zoneKind,
            shiftId,
            now);

        SessionChanged?.Invoke(this, CurrentUser);

        return new AuthenticationSuccessDto(CurrentUser, contextSource, message);
    }

    /// <summary>
    /// Odświeża strefę bieżącej sesji na podstawie najnowszego aktywnego przypisania.
    /// Po rotacji, którą wykonuje sam użytkownik, wpis w grafiku zmienia się w bazie —
    /// bez tego odświeżenia interfejs nadal wskazywałby starą strefę.
    /// </summary>
    public async Task RefreshCurrentUserContextAsync(CancellationToken cancellationToken = default)
    {
        var session = CurrentUser;

        if (session is null)
        {
            return;
        }

        var assignment = await _unitOfWork.StaffAssignments
            .GetActiveForUserAsync(session.Id, cancellationToken).ConfigureAwait(false);

        if (assignment is null)
        {
            return;
        }

        var zone = await _unitOfWork.Zones
            .GetByIdAsync(assignment.ZoneId, cancellationToken).ConfigureAwait(false);

        if (zone is null)
        {
            return;
        }

        CurrentUser = session with
        {
            CurrentZoneId = zone.Id,
            CurrentZoneName = zone.Name,
            CurrentZoneKind = zone.Kind
        };

        SessionChanged?.Invoke(this, CurrentUser);
    }

    public async Task<OperationResultDto> LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser is null)
        {
            return new OperationResultDto(false, "Brak aktywnej sesji.", null);
        }

        var user = CurrentUser;

        // Zwolnienie przypisania ze strefy zapisujemy jako wpis typu Release (historia rotacji).
        var assignments = _unitOfWork.StaffAssignments;
        var current = await assignments.GetActiveForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);

        if (current is not null)
        {
            current.Close(_clock.UtcNow);
        }

        var auditId = await _auditLog.RecordAsync(
            AuditActionType.Logout,
            user.Id,
            user.Login,
            user.Id,
            nameof(User),
            $"Wylogowanie użytkownika '{user.DisplayName}' ze strefy '{user.CurrentZoneName}'.",
            true,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        CurrentUser = null;
        SessionChanged?.Invoke(this, null);

        return new OperationResultDto(true, $"Wylogowano użytkownika {user.DisplayName}.", auditId);
    }

    /// <summary>
    /// Odczytuje strefę z grafiku (BR-16). W razie braku aktywnego dyżuru kieruje pracownika
    /// do modułu triage (strefa awaryjna) i informuje o tym fakcie użytkownika.
    /// </summary>
    private async Task<(Guid ZoneId, string ZoneName, ZoneKind ZoneKind, Guid? ShiftId, string Source, string Message)> ResolveZoneContextAsync(
        User user,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var zones = _unitOfWork.Zones;
        var assignments = _unitOfWork.StaffAssignments;

        var shift = await _unitOfWork.DutyShifts
            .GetActiveShiftAsync(user.Id, nowUtc, cancellationToken).ConfigureAwait(false);

        Zone zone;

        if (shift is not null)
        {
            zone = await zones.GetByIdAsync(shift.ZoneId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidZoneReassignmentException(
                    "Konflikt grafiku: zaplanowana strefa dyżuru nie istnieje. Skontaktuj się z koordynatorem.",
                    "SOR-DOM-023");

            var existing = await assignments.GetActiveForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);

            if (existing is not null && existing.ZoneId != zone.Id)
            {
                existing.Close(nowUtc);
            }

            if (existing is null || existing.ZoneId != zone.Id)
            {
                var assignment = StaffZoneAssignment.FromRoster(
                    _idGenerator.NewId(),
                    user.Id,
                    zone.Id,
                    nowUtc);

                await assignments.AddAsync(assignment, cancellationToken).ConfigureAwait(false);
            }

            await _auditLog.RecordAsync(
                AuditActionType.ZoneContextAssignedFromRoster,
                user.Id,
                user.Login,
                zone.Id,
                nameof(Zone),
                $"Strefa '{zone.Name}' ustawiona automatycznie z grafiku dyżuru {shift.Id:N}.",
                true,
                cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return (zone.Id, zone.Name, zone.Kind, shift.Id, "Grafik dyżurów",
                $"Strefa wyznaczona z grafiku: {zone.Name}.");
        }

        var triageZone = await zones.GetByCodeAsync("TRI", cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Zone), "TRI");

        return (triageZone.Id, triageZone.Name, triageZone.Kind, null, "Strefa awaryjna",
            $"Brak aktywnego wpisu w grafiku — dostęp przyznano do modułu triage ({triageZone.Name}). " +
            "Zmiana strefy jest możliwa po uzgodnieniu z koordynatorem.");
    }

    // ---------- Obsługa nieudanych prób ----------

    private bool IsLockedOut(string login)
    {
        if (_attemptTrackers.TryGetValue(login, out var tracker) && tracker.IsLockedOut(_clock.UtcNow))
        {
            return true;
        }

        return false;
    }

    private async Task<AuthenticationException> RecordFailureAsync(
        string login,
        string reason,
        string code,
        CancellationToken cancellationToken)
    {
        var tracker = _attemptTrackers.GetValueOrDefault(login) ?? new LoginAttemptTracker();
        tracker.RegisterFailure(_clock.UtcNow);

        if (tracker.FailedAttempts >= MaxFailedAttempts)
        {
            tracker.RegisterLockout(_clock.UtcNow, LockoutSeconds);
        }

        _attemptTrackers[login] = tracker;

        await _auditLog.RecordAsync(
            AuditActionType.LoginAttempt,
            null,
            login,
            null,
            nameof(User),
            $"Nieudana próba logowania ({tracker.FailedAttempts}): {reason}",
            false,
            cancellationToken).ConfigureAwait(false);

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new AuthenticationException(reason, code);
    }

    /// <summary>Wykonuje kosztowny hash, aby wyrównać czas odpowiedzi dla nieistniejących kont.</summary>
    private static void PerformDummyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        _ = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    }

    /// <summary>Stanowa klasa śledząca próby logowania per login.</summary>
    private sealed class LoginAttemptTracker
    {
        private DateTimeOffset? _lockedUntilUtc;

        public int FailedAttempts { get; private set; }

        public void RegisterFailure(DateTimeOffset nowUtc)
        {
            // Po upływie blokady licznik prób jest resetowany.
            if (_lockedUntilUtc is not null && nowUtc > _lockedUntilUtc.Value)
            {
                _lockedUntilUtc = null;
                FailedAttempts = 0;
            }

            FailedAttempts++;
        }

        public void RegisterLockout(DateTimeOffset nowUtc, int seconds) =>
            _lockedUntilUtc = nowUtc.AddSeconds(seconds);

        public bool IsLockedOut(DateTimeOffset nowUtc) =>
            _lockedUntilUtc is not null && nowUtc <= _lockedUntilUtc.Value;
    }
}