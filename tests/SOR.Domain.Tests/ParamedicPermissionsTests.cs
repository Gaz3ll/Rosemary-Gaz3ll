using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;

namespace SOR.Domain.Tests;

/// <summary>
/// Uprawnienia ratownika medycznego (BR-13). Ratownik realizuje zlecenia podania leku
/// oraz badania obrazowego, ale nie jest stroną decyzyjną: nie wystawia zleceń, nie stawia
/// rozpoznań, nie zamyka karty. Testy sprawdzają granicę obu stron oraz komplet kont.
/// </summary>
public sealed class ParamedicPermissionsTests : IAsyncLifetime
{
    private static readonly (string Login, string Password, string ZoneCode)[] ExpectedParamedics =
    [
        ("ratownik.emg", "SOR2026!remg", "EMG"),
        ("ratownik.int", "SOR2026!rint", "INT"),
        ("ratownik.trm", "SOR2026!rtrm", "TRM"),
    ];

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sor-paramedic-{Guid.NewGuid():N}.db");

    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IAuthenticationService _auth = null!;
    private IPatientService _patients = null!;
    private IMedicalBundleService _bundles = null!;
    private IIcd10CatalogService _icd10 = null!;
    private SorDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        _provider = services.BuildServiceProvider();

        await using (var bootstrap = _provider.CreateAsyncScope())
        {
            await bootstrap.ServiceProvider.InitializeDatabaseAsync();
        }

        _scope = _provider.CreateAsyncScope();

        _auth = _scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        _patients = _scope.ServiceProvider.GetRequiredService<IPatientService>();
        _bundles = _scope.ServiceProvider.GetRequiredService<IMedicalBundleService>();
        _icd10 = _scope.ServiceProvider.GetRequiredService<IIcd10CatalogService>();
        _context = _scope.ServiceProvider.GetRequiredService<SorDbContext>();
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            for (var attempt = 0; attempt < 5 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    await Task.Delay(50);
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
    }

    [Fact]
    public async Task SeedTworzyJednoKontoRatownikaWKazdejStrefieKlinicznej()
    {
        var paramedicLogins = await _context.Users
            .Where(user => user.Role == UserRole.Paramedic)
            .Select(user => user.Login)
            .ToListAsync();

        var expected = ExpectedParamedics.Select(account => account.Login).OrderBy(login => login);

        Assert.Equal(expected, paramedicLogins.OrderBy(login => login));
    }

    [Fact]
    public async Task RatownikNieMaKontaWStrefieTriage()
    {
        var paramedicZoneCodes = await (
            from user in _context.Users
            join assignment in _context.StaffZoneAssignments on user.Id equals assignment.UserId
            join zone in _context.Zones on assignment.ZoneId equals zone.Id
            where user.Role == UserRole.Paramedic
            select zone.Code)
            .ToListAsync();

        Assert.DoesNotContain("TRI", paramedicZoneCodes);
        Assert.Equal(3, paramedicZoneCodes.Count);
    }

    [Theory]
    [InlineData("ratownik.emg", "EMG", ZoneKind.Emergency)]
    [InlineData("ratownik.int", "INT", ZoneKind.Internal)]
    [InlineData("ratownik.trm", "TRM", ZoneKind.Trauma)]
    public async Task RatownikLogujeSieDoWlasnejStrefy(string login, string expectedZoneCode, ZoneKind expectedKind)
    {
        var password = ExpectedParamedics.Single(account => account.Login == login).Password;

        await _auth.LoginAsync(login, password);

        var user = _auth.CurrentUser;

        Assert.NotNull(user);
        Assert.Equal(UserRole.Paramedic, user!.Role);
        Assert.Equal(expectedKind, user.CurrentZoneKind);

        var zone = await _context.Zones.SingleAsync(zone => zone.Id == user.CurrentZoneId);
        Assert.Equal(expectedZoneCode, zone.Code);
    }

    [Theory]
    [InlineData(MedicalOrderType.Medication, "Adrenalina 1 mg i.v.")]
    [InlineData(MedicalOrderType.Imaging, "TK głowy bez kontrastu")]
    public async Task RatownikRealizujeZlecenieLekuIBadaniaObrazowego(MedicalOrderType type, string description)
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");
        var orderId = await AddOrderAsDoctorAsync(patientId, type, description);

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");
        var details = await _patients.ChangeOrderStateAsync(orderId, MedicalOrderState.Completed, null);

        var order = details.Orders.Single(item => item.Id == orderId);
        Assert.Equal(MedicalOrderState.Completed, order.State);
        Assert.NotNull(order.CompletedAtUtc);
    }

    [Theory]
    [InlineData(MedicalOrderType.Lab)]
    [InlineData(MedicalOrderType.Procedure)]
    [InlineData(MedicalOrderType.Consultation)]
    [InlineData(MedicalOrderType.Observation)]
    public async Task RatownikNieRealizujeZlecenInnychTypow(MedicalOrderType type)
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");
        var orderId = await AddOrderAsDoctorAsync(patientId, type, "Zlecenie testowe");

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");

        // Ograniczenie typu zlecenia jest regulą biznesową encji, a nie kwestią roli.
        await Assert.ThrowsAsync<ValidationException>(() =>
            _patients.ChangeOrderStateAsync(orderId, MedicalOrderState.Completed, null));
    }

    [Fact]
    public async Task RatownikNieWystawiaZlecenLekarskich()
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            _patients.AddOrderAsync(patientId, MedicalOrderType.Medication, "Furosemid 20 mg i.v.", false));

        Assert.Empty(await _context.MedicalOrders.Where(order => order.PatientId == patientId).ToListAsync());
    }

    [Fact]
    public async Task RatownikNieUstawiaRozpoznania()
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            _patients.SetDiagnosisAsync(patientId, "I10"));

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            _icd10.AssignAsync(patientId, "I10"));
    }

    [Fact]
    public async Task RatownikNieStosujePakietuMedycznego()
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");
        var bundleId = (await _bundles.GetAllAsync()).First().Id;

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");

        await Assert.ThrowsAsync<AuthorizationException>(() => _bundles.ApplyAsync(patientId, bundleId));
    }

    [Fact]
    public async Task RatownikNieZamykaKartyBezPotwierdzonegoTransportu()
    {
        var patientId = await CreateTreatedPatientAsync("ratownik.emg", "SOR2026!remg");

        await _auth.LoginAsync("ratownik.emg", "SOR2026!remg");

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            _patients.CloseCardAsync(patientId, transportCompleted: false));
    }

    [Fact]
    public async Task LekarzNadalRealizujeKazdeZlecenie()
    {
        var patientId = await CreateTreatedPatientAsync("lekarz.emg", "SOR2026!emg");

        var orderId = await AddOrderAsDoctorAsync(patientId, MedicalOrderType.Consultation, "Konsultacja chirurgiczna");

        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        var details = await _patients.ChangeOrderStateAsync(orderId, MedicalOrderState.Completed, null);

        Assert.Equal(MedicalOrderState.Completed, details.Orders.Single(item => item.Id == orderId).State);
    }

    [Fact]
    public async Task SeedUzupelniaKontaRatownikowWJuzyIstniejacejBazie()
    {
        // Symulacja wdrożenia na bazie, która powstała przed dodaniem roli ratownika:
        // konta i ich powiązania zostają usunięte, a następnie baza jest inicjalizowana ponownie.
        var paramedicIds = await _context.Users
            .Where(user => user.Role == UserRole.Paramedic)
            .Select(user => user.Id)
            .ToListAsync();

        _context.StaffZoneAssignments.RemoveRange(
            await _context.StaffZoneAssignments.Where(assignment => paramedicIds.Contains(assignment.UserId)).ToListAsync());
        _context.DutyShifts.RemoveRange(
            await _context.DutyShifts.Where(shift => paramedicIds.Contains(shift.UserId)).ToListAsync());
        _context.Users.RemoveRange(
            await _context.Users.Where(user => paramedicIds.Contains(user.Id)).ToListAsync());

        await _context.SaveChangesAsync();

        // Druga inicjalizacja przechodzi ścieżkę "baza zawiera już strefy", więc seedowanie
        // startowe jest pomijane i zadziałać może wyłącznie uzupełnianie kont.
        await using var secondScope = _provider.CreateAsyncScope();
        await secondScope.ServiceProvider.InitializeDatabaseAsync();

        await using var verificationScope = _provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<SorDbContext>();

        var restored = await verification.Users
            .Where(user => user.Role == UserRole.Paramedic)
            .Select(user => user.Login)
            .ToListAsync();

        Assert.Equal(
            ExpectedParamedics.Select(account => account.Login).OrderBy(login => login),
            restored.OrderBy(login => login));
    }

    /// <summary>Zakłada pacjenta w leczeniu w strefie lekarza, aby przetestować uprawnienia ratownika.</summary>
    private async Task<Guid> CreateTreatedPatientAsync(string doctorLogin, string doctorPassword)
    {
        await _auth.LoginAsync(doctorLogin, doctorPassword);

        var cards = await _patients.GetZonePatientsAsync(_auth.CurrentUser!.CurrentZoneId);
        return cards.First(card => card.State == PatientState.InTreatment).Id;
    }

    /// <summary>Zlecenie wystawiane zawsze przez lekarza — ratownik jedynie je realizuje.</summary>
    private async Task<Guid> AddOrderAsDoctorAsync(Guid patientId, MedicalOrderType type, string description)
    {
        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");

        var details = await _patients.AddOrderAsync(patientId, type, description, isUrgent: false);

        return details.Orders.Last(order => order.Type == type).Id;
    }
}