using SOR.Application.DTOs;
using SOR.Domain.Enums;

namespace SOR.Application.Interfaces;

/// <summary>Serwis uwierzytelniania (BR-15/BR-16).</summary>
public interface IAuthenticationService
{
    /// <summary>Uwierzytelnia użytkownika i ustawia kontekst strefy na podstawie grafiku.</summary>
    Task<AuthenticationSuccessDto> LoginAsync(string login, string password, CancellationToken cancellationToken = default);

    /// <summary>Kończy sesję i zwalnia przypisanie ze strefy.</summary>
    Task<OperationResultDto> LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Zwraca aktualnie zalogowanego użytkownika (null gdy brak sesji).</summary>
    AuthenticatedUserDto? CurrentUser { get; }

    /// <summary>
    /// Odświeża kontekst strefy bieżącej sesji na podstawie aktywnego przypisania.
    /// Wywoływane po rotacji, aby warstwa prezentacji natychmiast pracowała w nowej strefie (BR-16).
    /// </summary>
    Task RefreshCurrentUserContextAsync(CancellationToken cancellationToken = default);

    /// <summary>Zdarzenie sesji — używane przez warstwę prezentacji do nawigacji.</summary>
    event EventHandler<AuthenticatedUserDto?>? SessionChanged;
}

/// <summary>Serwis monitoringu obciążenia stref (BR-06/BR-07).</summary>
public interface IZoneLoadMonitoringService
{
    /// <summary>Pobiera bieżący stan obciążenia wszystkich stref oddziału.</summary>
    Task<DepartmentLoadSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Pobiera stan obciążenia jednej strefy.</summary>
    Task<ZoneLoadDto> GetZoneLoadAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Rozpoczyna cykliczne monitorowanie w tle (asynchroniczne, nieblokujące UI).</summary>
    Task StartMonitoringAsync(TimeSpan interval, CancellationToken cancellationToken = default);

    /// <summary>Zatrzymuje cykliczne monitorowanie.</summary>
    void StopMonitoring();

    /// <summary>Zdarzenie przekroczenia progu obciążenia strefy.</summary>
    event EventHandler<ZoneLoadDto>? ZoneOverloaded;

    /// <summary>Zdarzenie normalizacji obciążenia strefy.</summary>
    event EventHandler<ZoneLoadDto>? ZoneNormalized;

    /// <summary>Zdarzenie wygenerowania rekomendacji rotacji.</summary>
    event EventHandler<RotationRecommendationDto>? RotationSuggested;
}

/// <summary>Serwis rotacji personelu (BR-05/BR-07/BR-08).</summary>
public interface IStaffRotationService
{
    /// <summary>Manualna zmiana strefy przez użytkownika — z obowiązkowym uzasadnieniem i zapisem audytowym.</summary>
    Task<ReassignmentResultDto> ReassignZoneAsync(ReassignZoneCommand command, CancellationToken cancellationToken = default);

    /// <summary>Przyjęcie rekomendacji rotacji przez użytkownika.</summary>
    Task<ReassignmentResultDto> AcceptRecommendationAsync(AcceptRotationCommand command, CancellationToken cancellationToken = default);

    /// <summary>Rotacja zlecona przez koordynatora (innemu pracownikowi).</summary>
    Task<ReassignmentResultDto> CoordinatorAssignAsync(
        Guid targetUserId,
        Guid targetZoneId,
        ReassignmentReasonCode reasonCode,
        string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>Zwraca listę stref dostępnych do zmiany (filtry BR-04/BR-05).</summary>
    Task<IReadOnlyList<ZoneLoadDto>> GetReassignmentTargetsAsync(CancellationToken cancellationToken = default);

    /// <summary>Pobiera pełną historię rotacji danego pracownika.</summary>
    Task<IReadOnlyList<RotationHistoryDto>> GetRotationHistoryAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Pobiera listę personelu aktualnie przypisanego do stref.</summary>
    Task<IReadOnlyList<StaffAssignmentDto>> GetStaffByZoneAsync(Guid zoneId, CancellationToken cancellationToken = default);

    /// <summary>Bieżąca, przydatna dla warstwy UI rekomendacja rotacji (null gdy brak).</summary>
    RotationRecommendationDto? CurrentRecommendation { get; }
}