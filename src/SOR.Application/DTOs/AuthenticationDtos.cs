using SOR.Domain.Enums;

namespace SOR.Application.DTOs;

/// <summary>Podsumowanie sesji zalogowanego użytkownika — źródło kontekstu uprawnień w UI.</summary>
public sealed record AuthenticatedUserDto(
    Guid Id,
    string Login,
    string DisplayName,
    UserRole Role,
    Guid CurrentZoneId,
    string CurrentZoneName,
    ZoneKind CurrentZoneKind,
    Guid? DutyShiftId,
    DateTimeOffset LoggedInAtUtc)
{
    /// <summary>BR-12: koordynator ma dostęp do wszystkich stref oddziału.</summary>
    public bool CanManageAllZones => Role == UserRole.Coordinator;

    /// <summary>BR-13: rozpoznanie ICD-10 może wystawić wyłącznie lekarz lub koordynator.</summary>
    public bool CanEnterDiagnosis => Role is UserRole.Physician or UserRole.Coordinator;

    /// <summary>BR-13: zlecenie lekarskie może wystawić wyłącznie lekarz lub koordynator.</summary>
    public bool CanIssueOrders => Role is UserRole.Physician or UserRole.Coordinator;

    /// <summary>BR-13: transport potwierdza lekarz, pielęgniarka lub koordynator — nie ratownik.</summary>
    public bool CanConfirmTransport => Role is UserRole.Physician or UserRole.Coordinator or UserRole.Nurse;

    /// <summary>BR-13: zlecenia realizują lekarz, koordynator oraz ratownik medyczny.</summary>
    public bool CanExecuteOrders => Role is UserRole.Physician or UserRole.Coordinator or UserRole.Paramedic;

    /// <summary>Czy użytkownik jest zalogowany w podanej strefie (lub jest koordynatorem).</summary>
    public bool HasAccessTo(Guid zoneId) => CanManageAllZones || CurrentZoneId == zoneId;
}

/// <summary>Wynik nieudanej próby uwierzytelnienia (bez ujawniania szczegółów kryptograficznych).</summary>
public sealed record AuthenticationFailureDto(
    string Login,
    string Reason,
    int AttemptNumber,
    DateTimeOffset AttemptedAtUtc);

/// <summary>Komunikat zwracany po udanym logowaniu.</summary>
public sealed record AuthenticationSuccessDto(
    AuthenticatedUserDto User,
    string ZoneContextSource,
    string Message);