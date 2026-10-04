using SOR.Application.Interfaces;
using SOR.Domain.Common;

namespace SOR.Presentation.Services;

/// <summary>
/// Wyjątki domenowe i aplikacyjne tłumaczone na komunikaty zrozumiałe dla użytkownika.
/// Warstwa prezentacji przechwytuje jedyny typ bazowy i nie musi znać wewnętrznej struktury
/// wyjątków domenowych — jedynie odczytuje czytelny komunikat i kod błędu.
/// </summary>
public static class ExceptionMessageMapper
{
    public static UserFacingError Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ConcurrentPatientModificationException concurrency => new UserFacingError(
                "Karta jest zablokowana",
                $"{concurrency.Message} Aby uniknąć konfliktu, zadzwoń do tej osoby lub poczekaj na automatyczne zwolnienie blokady.",
                "warning"),

            PatientDischargeBlockedException discharge => new UserFacingError(
                "Nie można wypisać pacjenta z SOR",
                string.Join(Environment.NewLine, discharge.Reasons.Select(r => $"\u2022 {r}")),
                "warning"),

            InvalidZoneReassignmentException reassignment => new UserFacingError(
                "Zmiana strefy odrzucona",
                reassignment.Message,
                "warning"),

            ZoneCapacityExceededException capacity => new UserFacingError(
                "Brak wolnych miejsc w strefie docelowej",
                capacity.Message + " Wybierz inną strefę lub poczekaj na zwolnienie stanowiska.",
                "warning"),

            AuthorizationException authorization => new UserFacingError(
                "Brak uprawnień",
                authorization.Message,
                "error"),

            AuthenticationException authentication => new UserFacingError(
                "Nie udało się zalogować",
                authentication.Message,
                "error"),

            EntityNotFoundException notFound => new UserFacingError(
                "Nie znaleziono danych",
                notFound.Message,
                "error"),

            PersistenceException persistence => new UserFacingError(
                "Błąd zapisu w bazie danych",
                persistence.Message + " Jeśli problem będzie się powtarzał, zamknij aplikację i uruchom ją ponownie.",
                "error"),

            ValidationException validation => new UserFacingError(
                "Dane nieprawidłowe",
                validation.Message,
                "warning"),

            SorApplicationException application => new UserFacingError(
                "Operacja nie została wykonana",
                application.Message,
                "error"),

            DomainException domain => new UserFacingError(
                "Reguła biznesowa",
                domain.Message,
                "warning"),

            _ => new UserFacingError(
                "Nieoczekiwany błąd",
                $"Wystąpił nieprzewidziany błąd: {exception.Message}",
                "error")
        };
    }
}

/// <summary>Komunikat gotowy do wyświetlenia w interfejsie.</summary>
public sealed record UserFacingError(string Title, string Message, string Severity)
{
    /// <summary>Czy komunikat ma charakter informacyjny (toast) czy wymaga decyzji użytkownika.</summary>
    public bool RequiresAcknowledgement => Severity != "info";
}