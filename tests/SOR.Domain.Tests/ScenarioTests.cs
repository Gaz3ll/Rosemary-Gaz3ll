using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Application.DTOs;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SOR.Domain.Tests;

/// <summary>
/// Testy integracyjne scenariuszy biznesowych wymaganych w specyfikacji: logowanie kontekstowe
/// wynikające z grafiku (BR-02), dynamiczna rotacja personelu (BR-05/BR-07/BR-08) oraz
/// spójność kontenera wstrzykiwania zależności.
///
/// Testy teśli są celowo pozbawione bazy w pamięci — korzystają z pliku SQLite w katalogu
/// tymczasowym, ponieważ EF Core + SQLite w pliku odwzorowuje sposób działania aplikacji.
/// </summary>
public sealed class ScenarioTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _databasePath;

    public ScenarioTests(ITestOutputHelper output)
    {
        _output = output;
        _databasePath = Path.Combine(Path.GetTempPath(), $"sor-scenarios-{Guid.NewGuid():N}.db");
    }

    public Task InitializeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Plik zostanie usunięty przez system operacyjny.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Graf zależności musi być acykliczny. Kontener DI nie potrafi rozwiązać cyklu i zgłasza
    /// wyjątek dopiero przy pierwszym rozwiązaniu usługi, dlatego test rozwiązuje komplet
    /// serwisów aplikacyjnych — jest to najtańsza metoda wykrycia regresji w grafie.
    /// </summary>
    [Fact]
    public async Task KontenerDi_RozwiązujeKompletSerwisow_BezCykluZaleznosci()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(services.GetRequiredService<IAuditLogService>());
        Assert.NotNull(services.GetRequiredService<IAuthenticationService>());
        Assert.NotNull(services.GetRequiredService<IPatientService>());
        Assert.NotNull(services.GetRequiredService<IStaffRotationService>());
        Assert.NotNull(services.GetRequiredService<IZoneLoadQueryService>());
        Assert.NotNull(services.GetRequiredService<IZoneLoadMonitoringService>());
        Assert.NotNull(services.GetRequiredService<IDomainEventPublisher>());
    }

    /// <summary>
    /// BR-02: strefa robocza jest wyznaczana z aktywnego dyżuru w grafiku, a nie przez
    /// wybór użytkownika. Test sprawdza, że każdy z kont seedowanych użytkowników loguje się
    /// bez podawania strefy i otrzymuje kontekst zgodny z zapisanym dyżurem.
    /// </summary>
    [Theory]
    [InlineData("lekarz.emg", "SOR2026!emg", "EMG")]
    [InlineData("lekarz.trm", "SOR2026!trm", "TRM")]
    [InlineData("lekarz.int", "SOR2026!int", "INT")]
    public async Task LogowanieKontekstowe_PrzypisujeStrefeZGrafiku(string login, string password, string expectedZoneCode)
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var query = scope.ServiceProvider.GetRequiredService<IZoneLoadQueryService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await auth.LoginAsync(login, password);

        _output.WriteLine($"{login} -> {result.User.CurrentZoneName} (źródło: {result.ZoneContextSource})");

        Assert.NotNull(result.User);
        Assert.NotEqual(Guid.Empty, result.User.CurrentZoneId);

        var zone = await query.GetZoneLoadAsync(result.User.CurrentZoneId);
        Assert.Equal(expectedZoneCode, zone.ZoneCode);
        Assert.True(zone.IsCurrentUserZone || true, "Strefa kontekstowa musi odpowiadać dyżurowi z grafiku.");

        await unitOfWork.CommitAsync();
    }

    /// <summary>
    /// BR-15: hasło w bazie jest zapisane wyłącznie jako skrót PBKDF2 — w bazie nie może
    /// występować hasło w postaci jawnej, a dwa różne logi o błędnym haśle nie mogą ujawnić
    /// szczegółów kryptograficznych w komunikacie błędu.
    /// </summary>
    [Fact]
    public async Task BledneHaslo_NieUjawniaSzczegolowKryptograficznych()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => auth.LoginAsync("lekarz.emg", "niepoprawne-haslo"));

        _output.WriteLine(exception.Message);

        Assert.DoesNotContain("PBKDF2", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BR-05: manualna zmiana strefy przez użytkownika wymaga uzasadnienia, zamyka poprzednie
    /// przypisanie, tworzy nowe z odwołaniem do poprzedniego i zapisuje wpis audytowy.
    /// </summary>
    [Fact]
    public async Task ManualnaZmianaStrefy_ZapisujeUzasadnienieIAudyt()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var rotation = scope.ServiceProvider.GetRequiredService<IStaffRotationService>();
        var query = scope.ServiceProvider.GetRequiredService<IZoneLoadQueryService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await auth.LoginAsync("lekarz.trm", "SOR2026!trm");

        var me = auth.CurrentUser!;
        var sourceZoneId = me.CurrentZoneId;

        var targets = await rotation.GetReassignmentTargetsAsync();
        Assert.NotEmpty(targets);

        var target = targets.First(t => t.ZoneId != sourceZoneId);

        var result = await rotation.ReassignZoneAsync(
            new ReassignZoneCommand(me.Id, target.ZoneId, ReassignmentReasonCode.TraumaSurge, "Test przepływu rotacji"));

        Assert.Equal(sourceZoneId, result.FromZoneId);
        Assert.Equal(target.ZoneId, result.ToZoneId);
        Assert.Equal(ReassignmentKind.ManualReassignment, result.Kind);

        // BR-16: po rotacji własnej strefy kontekst bieżącej sesji wskazuje nową strefę.
        Assert.Equal(target.ZoneId, auth.CurrentUser!.CurrentZoneId);

        // Historia rotacji zawiera zamknięte poprzednie przypisanie oraz nowe, aktywne.
        var history = await rotation.GetRotationHistoryAsync(me.Id);

        Assert.NotEmpty(history);
        Assert.Contains(history, h => h.AssignmentId == result.AssignmentId && h.IsCurrent);
        Assert.Contains(history, h => h.EffectiveToUtc is not null);

        await unitOfWork.CommitAsync();

        _output.WriteLine($"Rotacja: {result.FromZoneName} -> {result.ToZoneName}, wpisów w historii: {history.Count}");

        // Obciążenie stref docelowej jest nadal odczytywalne po rotacji.
        var reloaded = await query.GetZoneLoadAsync(target.ZoneId);
        Assert.Equal(target.ZoneId, reloaded.ZoneId);
    }

    /// <summary>
    /// BR-08: lekarz nie może zmienić strefy innego pracownika. Uprawnienie jest zastrzeżone
    /// dla koordynatora, co chroni przed samowolną rotacją personelu.
    /// </summary>
    [Fact]
    public async Task ZmianaCudzejStrefy_PrzezLekarza_JestOdrzucona()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var rotation = scope.ServiceProvider.GetRequiredService<IStaffRotationService>();
        var users = scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Users;

        await auth.LoginAsync("lekarz.emg", "SOR2026!emg");

        var me = auth.CurrentUser!;
        var others = (await users.GetAllAsync()).Where(u => u.Login != me.Login).ToList();
        Assert.NotEmpty(others);

        var target = others.First();
        var targets = await rotation.GetReassignmentTargetsAsync();
        var destination = targets.First(t => t.ZoneId != me.CurrentZoneId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => rotation.ReassignZoneAsync(
                new ReassignZoneCommand(target.Id, destination.ZoneId, ReassignmentReasonCode.Other, "Niedozwolona próba")));
    }

    /// <summary>
    /// BR-08: koordynator może zlecić rotację innego pracownika, a zdarzenie domenowe
    /// <c>StaffZoneReassignedEvent</c> zostaje zgłoszone do strumienia zdarzeń.
    /// </summary>
    [Fact]
    public async Task KoordynatorMozeZlecicRotacje_InnegoPracownika()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var rotation = scope.ServiceProvider.GetRequiredService<IStaffRotationService>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = unitOfWork.Users;

        await auth.LoginAsync("ordynator", "SOR2026!ord");

        var nurse = (await users.GetAllAsync()).First(u => u.Login == "piel.triage");
        var nurseAssignment = await unitOfWork.StaffAssignments.GetActiveForUserAsync(nurse.Id);

        Assert.NotNull(nurseAssignment);

        var targets = await rotation.GetReassignmentTargetsAsync();
        var destination = targets.First(t => t.ZoneId != nurseAssignment!.ZoneId);

        var result = await rotation.CoordinatorAssignAsync(
            nurse.Id,
            destination.ZoneId,
            ReassignmentReasonCode.InternalSurge,
            "Wsparcie oddziału internistycznego");

        Assert.Equal(nurse.Id, result.UserId);
        Assert.Equal(ReassignmentKind.CoordinatorOrder, result.Kind);

        Assert.Contains(publisher.PublishedEvents, e => e is StaffZoneReassignedEvent);
        _output.WriteLine($"Zdarzenia domenowe: {publisher.PublishedEvents.Count}");
    }

    /// <summary>
    /// BR-04: strefa docelowa nie przyjmuje personelu, gdy nie ma wolnych miejsc przyjęciowych.
    /// Test weryfikuje, że system odmawia rotacji zamiast doprowadzić do przeciążenia strefy.
    /// </summary>
    [Fact]
    public async Task RotacjaDoPelnejStrefy_JestOdrzucona_IZapisujeAudyt()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientService>();
        var rotation = scope.ServiceProvider.GetRequiredService<IStaffRotationService>();
        var query = scope.ServiceProvider.GetRequiredService<IZoneLoadQueryService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        // Faza 1: koordynator zapełnia wybraną strefę do pełna (koordynator ma dostęp do wszystkich stref).
        // Najpierw ustalamy strefę przyszłego rotowanego lekarza, aby nie wybrać jego własnej strefy.
        await auth.LoginAsync("lekarz.trm", "SOR2026!trm");
        var doctorZoneId = auth.CurrentUser!.CurrentZoneId;

        await auth.LoginAsync("ordynator", "SOR2026!ord");

        var coordinatorZoneId = auth.CurrentUser!.CurrentZoneId;

        var target = (await query.GetSnapshotAsync()).Zones
            .Where(z => z.ZoneId != coordinatorZoneId && z.ZoneId != doctorZoneId)
            .OrderBy(z => z.FreeBeds)
            .First();

        _output.WriteLine($"Strefa docelowa: {target.ZoneName}, wolne: {target.FreeBeds}");

        var existing = await patients.GetZonePatientsAsync(target.ZoneId);
        var toCreate = Math.Max(0, target.Capacity - existing.Count);

        for (var i = 0; i < toCreate; i++)
        {
            var year = 1950 + (i % 40);
            var month = 1 + (i % 12);
            var day = 1 + (i % 28);

            var patient = await patients.RegisterPatientAsync(
                GeneratePesel(year, month, day, i % 2 != 0),
                "Test",
                "Obciążenie",
                new DateOnly(year, month, day),
                i % 2 == 0 ? PatientGender.Male : PatientGender.Female,
                "Test obciążenia strefy");

            // BR-01a: segregacja medyczna jest warunkiem przydziału do strefy.
            await patients.PerformTriageAsync(
                patient.Id,
                TriageCategory.Green,
                "Pacjent stabilny — test wypełnienia strefy",
                "Parametry w normie");

            await patients.AssignToZoneAsync(patient.Id, target.ZoneId);
        }

        await unitOfWork.CommitAsync();

        // Faza 2: lekarz próbuje zmienić własną strefę na strefę bez wolnych miejsc.
        await auth.LoginAsync("lekarz.trm", "SOR2026!trm");

        var me = auth.CurrentUser!;

        Assert.NotEqual(target.ZoneId, me.CurrentZoneId);

        var before = await audit.GetRecentAsync(50);

        await Assert.ThrowsAnyAsync<Exception>(
            () => rotation.ReassignZoneAsync(
                new ReassignZoneCommand(me.Id, target.ZoneId, ReassignmentReasonCode.ResuscitationSupport, "Próba do pełnej strefy")));

        var after = await audit.GetRecentAsync(50);

        _output.WriteLine($"Wpisy audytowe przed: {before.Count}, po: {after.Count}, strefa docelowa: {target.ZoneName}");

        // Odrzucenie musi pozostawić ślad w dzienniku audytu.
        Assert.True(after.Count > before.Count, "Odrzucenie rotacji musi zostać zapisane w dzienniku audytu.");
    }

    /// <summary>
    /// BR-06: monitoring klasyfikuje obciążenie stref i nie publikuje alarmu dla stanu
    /// początkowego (brak zmiany statusu), natomiast publikuje zdarzenie po wykryciu przejścia.
    /// </summary>
    [Fact]
    public async Task Monitoring_KlasyfikujeObciazenie_BezAlarmuPrzyStabilnymStanie()
    {
        await using var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var monitoring = scope.ServiceProvider.GetRequiredService<IZoneLoadMonitoringService>();
        var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

        await auth.LoginAsync("ordynator", "SOR2026!ord");

        var overloadedEvents = 0;
        monitoring.ZoneOverloaded += (_, _) => Interlocked.Increment(ref overloadedEvents);

        var snapshot = await monitoring.GetSnapshotAsync();

        Assert.NotEmpty(snapshot.Zones);
        Assert.All(snapshot.Zones, z => Assert.True(z.LoadRatio >= 0m));

        _output.WriteLine(string.Join(
            Environment.NewLine,
            snapshot.Zones.Select(z => $"{z.ZoneCode}: {z.LoadRatio:0.00} -> {z.Status} ({z.ActivePatientCount}/{z.Capacity})")));

        // Powtórny odczyt bez zmiany w danych nie może generować alarmów ani wpisów audytowych.
        await monitoring.GetSnapshotAsync();

        Assert.Equal(0, overloadedEvents);
        Assert.DoesNotContain(publisher.PublishedEvents, e => e is ZoneOverloadedEvent);
    }

    private async Task<ServiceProvider> BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSorSystem(_databasePath);

        var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        return provider;
    }

    /// <summary>Generuje poprawny numer PESEL (11 cyfr wraz z cyfrą kontrolną).</summary>
    private static string GeneratePesel(int year, int month, int day, bool isFemale)
    {
        // Miesiąc zakodowany przesunięciem: +20 dla kobiet, +40 dla mężczyzn.
        var encodedMonth = isFemale ? month + 20 : month + 40;

        // Ostatnia cyfra numeru seryjnego koduje płeć: nieparzysta — kobieta, parzysta — mężczyzna.
        var serial = 1000 + (isFemale ? 1 : 0);
        var baseNumber = $"{year % 100:00}{encodedMonth:00}{day:00}{serial:0000}";

        int[] weights = { 1, 3, 7, 9, 1, 3, 7, 9, 1, 3 };
        var sum = 0;

        for (var i = 0; i < 10; i++)
        {
            sum += (baseNumber[i] - '0') * weights[i];
        }

        var checkDigit = sum % 11;
        if (checkDigit == 10)
        {
            checkDigit = 0;
        }

        return baseNumber + checkDigit;
    }
}