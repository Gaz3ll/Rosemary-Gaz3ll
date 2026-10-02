using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SOR.Domain.Entities;
using SOR.Domain.ValueObjects;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;

namespace SOR.Domain.Tests;

/// <summary>Tymczasowa weryfikacja generatora pacjentów (usunięta po sprawdzeniu).</summary>
public sealed class TempPatientGeneratorVerification : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sor-gen-verify-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeDatabaseAsync();

        await RunGenerator("--count", "14", "--awaiting", "3", "--closed", "3", "--seed", "5", "--db", _databasePath);
        await RunGenerator("--count", "6", "--zone", "TRM", "--category", "Red", "--seed", "9", "--db", _databasePath);
    }

    [Fact]
    public async Task WszyscyWygenerowaniPacjenciMajaPoprawnePeseleICzescioweKarty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        var patients = await context.Patients
            .Include(p => p.TriageHistory)
            .Include(p => p.Orders)
            .AsNoTracking()
            .ToListAsync();

        var seedPesels = new[] { "85441410008", "90311210011", "72430810026", "55292110034", "88420410040", "63271710054", "78451910063", "34210610077", "41482310086", "96431810105", "69222610118", "11310510128" };

        var generated = patients.Where(p => !seedPesels.Contains(p.Pesel)).ToList();

        Assert.Equal(26, generated.Count);

        foreach (var patient in patients)
        {
            // PESEL musi przechodzić walidację domenową i zgadzać się z datą oraz płcią.
            Assert.True(PeselNumber.TryParse(patient.Pesel, out var parsed));
            Assert.NotNull(parsed);
            Assert.True(parsed!.MatchesDateOfBirth(patient.DateOfBirth), $"PESEL {patient.Pesel} vs {patient.DateOfBirth:yyyy-MM-dd}");
            Assert.Equal(
                (patient.Gender == Domain.Enums.PatientGender.Male) == ((patient.Pesel[9] - '0') % 2 == 0),
                true);

            if (patient.State != Domain.Enums.PatientState.Registered)
            {
                Assert.NotEmpty(patient.TriageHistory);
            }

            if (patient.State == Domain.Enums.PatientState.Closed)
            {
                Assert.Null(patient.ZoneId);
                Assert.NotNull(patient.ClosedAtUtc);
            }
            else if (patient.State == Domain.Enums.PatientState.Registered)
            {
                Assert.Empty(patient.TriageHistory);
                Assert.Equal("TRI", ZoneCode(context, patient));
            }
            else
            {
                Assert.NotNull(patient.ZoneId);
                Assert.Null(patient.ClosedAtUtc);
            }

            Assert.True(patient.RegisteredAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1), "rejestracja w przyszłości");

            foreach (var assessment in patient.TriageHistory)
            {
                Assert.True(assessment.AssessedAtUtc >= patient.RegisteredAtUtc, "triage przed rejestracją");
            }

            foreach (var order in patient.Orders)
            {
                Assert.True(order.OrderedAtUtc >= patient.RegisteredAtUtc, "zlecenie przed rejestracją");
                Assert.True(order.OrderedAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1), "zlecenie w przyszłości");
                Assert.NotEmpty(order.Description);
            }
        }

        // Zlecenia nie mogą dublować się po ponownym uruchomieniu z tym samym ziarnem.
        var pesels = patients.Select(p => p.Pesel).ToList();
        Assert.Equal(pesels.Count, pesels.Distinct().Count());

        var zones = await context.Zones.Select(z => new { z.Code, z.Capacity }).ToListAsync();
        var active = patients.Count(p => p.State is Domain.Enums.PatientState.Triaged or Domain.Enums.PatientState.InTreatment);

        foreach (var zone in zones)
        {
            var inZone = patients.Count(p => ZoneCode(context, p) == zone.Code);
            Assert.True(inZone <= zone.Capacity, $"strefa {zone.Code}: {inZone} pacjentów przy pojemności {zone.Capacity}");
        }

        Assert.True(active > 0);
    }

    [Fact]
    public async Task PonowneUruchomienieZTymSamymZiarnemNieDodajeDuplikatow()
    {
        var before = await CountPatients();
        await RunGenerator("--count", "14", "--awaiting", "3", "--closed", "3", "--seed", "5", "--db", _databasePath);
        var after = await CountPatients();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SkryptSqlTezWczytujeSieDoBazy()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"sor-gen-verify-{Guid.NewGuid():N}.sql");
        var sqlPath = Path.Combine(Path.GetTempPath(), $"sor-gen-verify-{Guid.NewGuid():N}.db");

        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"sor-gen-sqltest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            var scripts = new List<string>();

            for (var index = 0; index < 2; index++)
            {
                var path = Path.Combine(scriptDirectory, $"pacjenci-{index}.sql");
                await RunGenerator("--count", "5", "--seed", $"{100 + index}", "--sql", path, "--now", "2026-10-02T12:00:00Z", "--db", _databasePath);
                scripts.Add(path);
            }

            var target = Path.Combine(scriptDirectory, "kopia.db");
            File.Copy(_databasePath, target);

            foreach (var script in scripts)
            {
                await ApplyScript(script, target);
            }

            var count = CountPatientsIn(target);

            Assert.Equal(12 + 10, count);

            // Ten sam skrypt wykonany ponownie nie może dodać pacjentów.
            await ApplyScript(scripts[0], target);

            Assert.Equal(count, CountPatientsIn(target));
        }
        finally
        {
            foreach (var path in new[] { scriptPath, sqlPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    private static string? ZoneCode(SorDbContext context, Patient patient)
    {
        if (patient.ZoneId is null)
        {
            return null;
        }

        return context.Zones.Where(z => z.Id == patient.ZoneId).Select(z => z.Code).SingleOrDefault();
    }

    private static async Task ApplyScript(string scriptPath, string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(databasePath);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        await context.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(scriptPath));
    }

    private async Task<int> CountPatients()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(_databasePath);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        return await context.Patients.CountAsync();
    }

    private static int CountPatientsIn(string path)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSorSystem(path);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        return context.Patients.Count();
    }

    private static Task RunGenerator(params string[] arguments)
    {
        var dll = @"C:\Users\wikto\Documents\SOR-PatientGenerator\bin\Debug\net9.0\sor-patients.dll";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(dll);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        Assert.True(process.ExitCode == 0, $"Generator zakończony kodem {process.ExitCode}. stdout: {output} stderr: {error}");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }

        return Task.CompletedTask;
    }
}
