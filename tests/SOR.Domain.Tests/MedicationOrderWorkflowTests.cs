using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.Enums;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;

namespace SOR.Domain.Tests;

/// <summary>
/// Testy zlecania leków z formularza — dokładnie tej ścieżki, którą wywołuje przycisk
/// „Zleć podanie" w interfejsie. Zabezpieczają przed sytuacją, w której zlecenie dodane
/// do kolekcji pacjenta, ale nie jawnie do kontekstu, jest przez EF traktowane jak istniejący
/// wiersz (UPDATE zamiast INSERT) i kończy się błędem współbieżności.
/// </summary>
public sealed class MedicationOrderWorkflowTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sor-orders-{Guid.NewGuid():N}.db");

    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IPatientService _patients = null!;
    private SorDbContext _context = null!;
    private Guid _patientId;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        _provider = services.BuildServiceProvider();

        // Jedna instancja bazy i jedno wywołanie inicjalizacji — tak jak w aplikacji,
        // gdzie DI tworzy pojedynczy zakres na cały czas życia procesu.
        await using (var bootstrap = _provider.CreateAsyncScope())
        {
            await bootstrap.ServiceProvider.InitializeDatabaseAsync();
        }

        _scope = _provider.CreateAsyncScope();

        var auth = _scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        _patients = _scope.ServiceProvider.GetRequiredService<IPatientService>();
        _context = _scope.ServiceProvider.GetRequiredService<SorDbContext>();

        await auth.LoginAsync("lekarz.emg", "SOR2026!emg");

        var cards = await _patients.GetZonePatientsAsync(auth.CurrentUser!.CurrentZoneId);
        _patientId = cards.First(c => c.State == PatientState.InTreatment).Id;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Pliki dziennika WAL i indeksu SQLite musi zniknąć razem z bazą.
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
    public async Task ZlecenieLeku_ZFormularza_JestZapisaneWRejestrzeZleceń()
    {
        // Opis składany przez formularz leków: nazwa preparatu, dawka, droga podania.
        await _patients.AddOrderAsync(
            _patientId,
            MedicalOrderType.Medication,
            "Adrenalina 1 mg/ml — 1 mg i.v. (Dożylnie)",
            isUrgent: true);

        var order = await _context.MedicalOrders.SingleAsync(o => o.PatientId == _patientId);

        Assert.Equal(MedicalOrderType.Medication, order.Type);
        Assert.Equal(MedicalOrderState.Open, order.State);
        Assert.True(order.IsUrgent);
        Assert.Contains("Adrenalina", order.Description);
    }

    [Fact]
    public async Task DwaZleceniaLeku_PodRzad_ZapisujaObaWiersze()
    {
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Adrenalina 1 mg i.v.", true);
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Furosemid 20 mg i.v.", false);

        var orders = await _context.MedicalOrders
            .Where(o => o.PatientId == _patientId)
            .OrderBy(o => o.OrderedAtUtc)
            .ToListAsync();

        Assert.Equal(2, orders.Count);
        Assert.True(orders[0].IsUrgent);
        Assert.False(orders[1].IsUrgent);
    }

    [Fact]
    public async Task ZlecenieLeku_NadpisujeIstniejaceZlecenieBezBleduWspolpracy()
    {
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Adrenalina 1 mg i.v.", true);
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Lab, "Morfologia krwi", false);
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Furosemid 20 mg i.v.", false);

        var count = await _context.MedicalOrders.CountAsync(o => o.PatientId == _patientId);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task KartaZablokowanaPrzezInnegoPracownika_NiePrzyjmujeZlecenia()
    {
        await _patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Adrenalina 1 mg i.v.", true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<IPatientService>();


        await auth.LoginAsync("ordynator", "SOR2026!ord");

        // Karta pozostaje zablokowana na lekarza, który zlecił lek (BR-20).
        await Assert.ThrowsAsync<ConcurrentPatientModificationException>(() =>
            patients.AddOrderAsync(_patientId, MedicalOrderType.Medication, "Kwas acetylosalicylowy 150 mg p.o.", false));
    }
}
