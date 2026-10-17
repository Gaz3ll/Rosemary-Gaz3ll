using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Domain.Common;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace SOR.Domain.Tests;

/// <summary>
/// Testy integracyjne warstwy trwałości: weryfikują poprawność modelu EF Core,
/// tworzenie schematu SQLite oraz trwałość operacji rotacji personelu.
/// Używają bazy tymczasowej w katalogu systemowym, aby nie dotykać bazy aplikacji.
/// </summary>
public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _databasePath;

    public PersistenceTests(ITestOutputHelper output)
    {
        _output = output;
        _databasePath = Path.Combine(Path.GetTempPath(), $"sor-tests-{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnectionRelease(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureCreated_TworzyPoprawnySchemat_BezBledowModelu()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        var zones = await context.Zones.CountAsync();
        var users = await context.Users.CountAsync();
        var patients = await context.Patients.CountAsync();
        var assignments = await context.StaffZoneAssignments.CountAsync();
        var shifts = await context.DutyShifts.CountAsync();

        _output.WriteLine($"Strefy: {zones}, Użytkownicy: {users}, Pacjenci: {patients}, Przypisania: {assignments}, Dyżury: {shifts}");

        Assert.Equal(4, zones);
        Assert.True(users >= 6);
        Assert.True(patients >= 10);
        Assert.True(assignments >= 6);
        Assert.True(shifts >= 6);
    }

    [Fact]
    public async Task Seed_JestIdempotentny_NieDuplikujeDanych()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        await provider.InitializeDatabaseAsync();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        Assert.Equal(4, await context.Zones.CountAsync());
    }

    [Fact]
    public async Task DiagnozaIcd10_JestZapisywanaWRelacjiOwnedType()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        var withDiagnosis = await context.Patients
            .FirstOrDefaultAsync(p => p.Diagnosis != null);

        Assert.NotNull(withDiagnosis);
        Assert.False(string.IsNullOrWhiteSpace(withDiagnosis!.Diagnosis!.Value));
    }

    [Fact]
    public async Task UzasadnienieZmianyStrefy_JestZapisaneWRepozytorium()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var ids = new GuidIdGenerator();

        var traumaDoctor = await context.Users.FirstAsync(u => u.Login == "lekarz.trm");
        var emergency = await context.Zones.FirstAsync(z => z.Code == "EMG");

        var original = await context.StaffZoneAssignments
            .FirstAsync(a => a.UserId == traumaDoctor.Id && a.EffectiveToUtc == null);

        var reason = Domain.ValueObjects.ReassignmentReason.Create(
            Domain.Enums.ReassignmentReasonCode.ResuscitationSupport,
            "Wsparcie resuscytacji — wzrost napływu pacjentów urazowych",
            clock.UtcNow,
            clock);

        var manual = Domain.Entities.StaffZoneAssignment.Manual(
            ids.NewId(),
            traumaDoctor.Id,
            emergency.Id,
            reason,
            clock.UtcNow,
            original.Id);

        original.Close(clock.UtcNow);
        context.StaffZoneAssignments.Add(manual);
        await context.SaveChangesAsync();

        var reloaded = await context.StaffZoneAssignments.FirstAsync(a => a.Id == manual.Id);

        Assert.NotNull(reloaded.Reason);
        Assert.Equal(Domain.Enums.ReassignmentReasonCode.ResuscitationSupport, reloaded.Reason!.Code);
        Assert.Equal(Domain.Enums.ReassignmentKind.ManualReassignment, reloaded.Kind);
        Assert.Equal(original.Id, reloaded.SupersedesAssignmentId);

        // Nowe przypisanie jest aktywne; poprzednie zostało zamknięte w tym samym zapisie.
        Assert.True(reloaded.IsActive);
        Assert.Null(reloaded.EffectiveToUtc);

        var reloadedClosed = await context.StaffZoneAssignments.FirstAsync(a => a.Id == original.Id);
        Assert.NotNull(reloadedClosed.EffectiveToUtc);
        Assert.False(reloadedClosed.IsActive);
    }

    [Fact]
    public async Task DziennikAudytu_ZapisujeNieudanaProbęLogowania()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuditLogService>();
        var entries = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IUnitOfWork>();

        var id = await audit.RecordAsync(
            Domain.Enums.AuditActionType.LoginAttempt,
            null,
            "nieznany",
            null,
            "User",
            "Nieudana próba logowania (1): Nieprawidłowy login lub hasło.",
            false);

        await entries.CommitAsync();

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyUnitOfWork = verifyScope.ServiceProvider.GetRequiredService<Application.Interfaces.IUnitOfWork>();
        var saved = await verifyUnitOfWork.AuditEntries.GetByIdAsync(id);

        Assert.NotNull(saved);

        // Wpis o nieudanej próbie logowania musi być oznaczony jako niepowodzenie.
        Assert.False(saved!.Success);
        Assert.True(saved.IsFailure);
        Assert.Equal("nieznany", saved.ActorLogin);
    }

    [Fact]
    public async Task SchematNieaktualny_PowodujeOdtworzenieBazyIKatalogow()
    {
        // Symulacja bazy z poprzedniej wersji aplikacji: plik istnieje, ale brakuje
        // w nim tabel katalogu leków, pakietów i ICD-10 dodanych w nowej wersji.
        var services = BuildServices();
        await using (var provider = services.BuildServiceProvider())
        {
            await provider.InitializeDatabaseAsync();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Kolejność ma znaczenie — tabele podrzędne usuwamy przed nadrzędnymi (klucze obce).
        var staleTables = new[]
        {
            "MedicalBundleItems",
            "MedicationAdministrations",
            "MedicalBundles",
            "Medications",
            "Icd10Catalog"
        };

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();

            foreach (var table in staleTables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS \"{table}\";";
                command.ExecuteNonQuery();
            }
        }

        // Ponowne uruchomienie aplikacji musi odtworzyć schemat i dane referencyjne.
        await using (var provider = services.BuildServiceProvider())
        {
            await provider.InitializeDatabaseAsync();
        }

        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        Assert.Equal(4, await context.Zones.CountAsync());
        Assert.True(await context.Medications.CountAsync() > 0);
        Assert.True(await context.Icd10CatalogEntries.CountAsync() > 0);
        Assert.True(await context.MedicalBundles.CountAsync() > 0);
    }

    /// <summary>
    /// Filtr „Aktualnie w izbie” zwraca wyłącznie pacjentów przypisanych do strefy;
    /// zawężenie zakresu strefy (ZoneScopeId) redukuje wynik do wskazanej strefy,
    /// a pacjent jeszcze nieztriagowany (bez strefy) pozostaje „aktualnie w szpitalu”.
    /// </summary>
    [Fact]
    public async Task FiltrAktualnieWIzbie_ZwracaTylkoPacjentowPrzypisanychDoStrefy()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var stays = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientStayService>();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var from = nowUtc.AddDays(-2);
        var to = nowUtc.AddDays(2);

        // Pacjent po rejestracji, przed Triage — nie ma przypisanej strefy (BR-01a).
        var untriaged = Domain.Entities.Patient.Register(
            Guid.NewGuid(),
            "90051500000",
            "Adam",
            "BezStrefy",
            new DateOnly(1990, 5, 15),
            Domain.Enums.PatientGender.Male,
            null,
            nowUtc);
        context.Patients.Add(untriaged);
        await context.SaveChangesAsync();

        var unscoped = await stays.GetStaysAsync(
            new Application.DTOs.StayQuery(
                from,
                to,
                Application.DTOs.StayFilter.CurrentlyInBay,
                WithoutRefusalCard: false),
            nowUtc);

        Assert.NotEmpty(unscoped);
        Assert.All(unscoped, stay => Assert.NotNull(stay.ZoneId));
        Assert.DoesNotContain(unscoped, stay => stay.PatientId == untriaged.Id);

        var zoneId = unscoped[0].ZoneId!.Value;
        var scoped = await stays.GetStaysAsync(
            new Application.DTOs.StayQuery(
                from,
                to,
                Application.DTOs.StayFilter.CurrentlyInBay,
                WithoutRefusalCard: false,
                ZoneScopeId: zoneId),
            nowUtc);

        Assert.NotEmpty(scoped);
        Assert.All(scoped, stay => Assert.Equal(zoneId, stay.ZoneId));
        Assert.True(scoped.Count < unscoped.Count);

        var inHospital = await stays.GetStaysAsync(
            new Application.DTOs.StayQuery(
                from,
                to,
                Application.DTOs.StayFilter.CurrentlyInHospital,
                WithoutRefusalCard: false),
            nowUtc);

        Assert.Contains(inHospital, stay => stay.PatientId == untriaged.Id);
    }

    /// <summary>
    /// Seeder demonstracyjny wariantuje dane administracyjne kolumn listy pobytów
    /// (Księga główna, Ubezp., Onkologiczna, Zgoda, IDH, COVID-19, Nr), a filtr COVID-19
    /// zawęża wynik do pacjentów z flagą przyjęcia z powodu COVID-19.
    /// </summary>
    [Fact]
    public async Task Seeder_WariantujeDaneAdministracyjne_AFiltrCovid19Dziala()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var stays = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientStayService>();
        var nowUtc = DateTimeOffset.UtcNow;
        var from = nowUtc.AddDays(-2);
        var to = nowUtc.AddDays(2);

        var all = await stays.GetStaysAsync(
            new Application.DTOs.StayQuery(
                from,
                to,
                Application.DTOs.StayFilter.All,
                WithoutRefusalCard: false),
            nowUtc);

        Assert.NotEmpty(all);
        Assert.Contains(all, stay => stay.HasInsurance == true);
        Assert.Contains(all, stay => stay.HasInsurance == false);
        Assert.Contains(all, stay => stay.HasInsurance is null);
        Assert.Contains(all, stay => stay.IsOncological);
        Assert.Contains(all, stay => stay.ConsentGiven is false);
        Assert.Contains(all, stay => stay.ConsentGiven is null);
        Assert.Contains(all, stay => stay.MainLedgerBook != "—");
        Assert.Contains(all, stay => stay.MainLedgerBook == "—");
        Assert.Contains(all, stay => stay.IdhNumber != "—");
        Assert.Contains(all, stay => stay.IdhNumber == "—");
        Assert.Contains(all, stay => stay.VisitNumber > 1);

        var covid = await stays.GetStaysAsync(
            new Application.DTOs.StayQuery(
                from,
                to,
                Application.DTOs.StayFilter.Covid19,
                WithoutRefusalCard: false),
            nowUtc);

        Assert.NotEmpty(covid);
        Assert.All(covid, stay => Assert.True(stay.Covid19));
        Assert.True(covid.Count < all.Count);
    }

    private ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSorSystem(_databasePath);
        return services;
    }

    private static void SqliteConnectionRelease(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Plik zostanie usunięty przez system operacyjny.
        }
    }
}