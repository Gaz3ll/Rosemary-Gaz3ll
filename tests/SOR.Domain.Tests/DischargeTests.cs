using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SOR.Application.Interfaces;
using SOR.Domain.Common;
using SOR.Domain.DomainServices;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;

namespace SOR.Domain.Tests;

/// <summary>
/// Wypis pacjenta z SOR (BR-11) w trzech scenariuszach: zakończenie leczenia (wypis do domu),
/// wypis na własne żądanie oraz przekazanie na inny oddział szpitala. Testy domenowe pilnują
/// reguł blokujących, a testy integracyjne — decyzji lekarza, katalogu oddziałów i audytu.
/// </summary>
public sealed class DischargeTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sor-discharge-{Guid.NewGuid():N}.db");

    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IAuthenticationService _auth = null!;
    private IPatientService _patients = null!;
    private IDepartmentCatalogService _departments = null!;
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
        _departments = _scope.ServiceProvider.GetRequiredService<IDepartmentCatalogService>();
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
                    await Task.Delay(50);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Reguły domenowe
    // -----------------------------------------------------------------------

    [Fact]
    public void ZakonczenieLeczenia_BezRozpoznania_JestZablokowane()
    {
        var patient = PatientInTreatment();
        patient.AddOrder(CompletedOrder(patient.Id));

        var context = new PatientDischargeContext(
            patient.Id,
            DischargeType.TreatmentCompleted,
            HasIcd10Diagnosis: false,
            OpenOrderCount: 0,
            patient.State,
            HasTargetDepartment: false,
            HasReason: false);

        Assert.Contains(
            PatientDischargePolicy.GetBlockingReasons(context),
            reason => reason.Contains("rozpozn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ZakonczenieLeczenia_BlokujeOtwarteZlecenia()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("I21.4"));
        patient.AddOrder(OpenOrder(patient.Id));

        var blockers = patient.GetDischargeBlockers(DischargeType.TreatmentCompleted);

        Assert.NotEmpty(blockers);
        Assert.Throws<PatientDischargeBlockedException>(() =>
            patient.Discharge(
                Guid.NewGuid(),
                DischargeType.TreatmentCompleted,
                DateTimeOffset.UtcNow,
                Guid.NewGuid(),
                "lekarz"));
    }

    [Fact]
    public void ZakonczenieLeczenia_PrzySpelnionychWarunkach_UstawiaStanZamkniety()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("I21.4"));
        patient.AddOrder(CompletedOrder(patient.Id));

        var discharge = patient.Discharge(
            Guid.NewGuid(),
            DischargeType.TreatmentCompleted,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "lekarz.emg");

        Assert.Equal(PatientState.Closed, patient.State);
        Assert.Equal(DischargeType.TreatmentCompleted, discharge.Type);
        Assert.Null(discharge.DepartmentId);
        Assert.Same(discharge, Assert.Single(patient.Discharges));
        Assert.NotNull(patient.ClosedAtUtc);
    }

    [Fact]
    public void WypisNaWlasneZadanie_BezPowodu_JestZablokowany()
    {
        var patient = PatientInTreatment();

        var blockers = patient.GetDischargeBlockers(DischargeType.AtPatientRequest, hasReason: false);

        Assert.NotEmpty(blockers);
    }

    [Fact]
    public void WypisNaWlasneZadanie_AnulujeOtwarteZleceniaIZapisujePowod()
    {
        var patient = PatientInTreatment();
        var order = patient.AddOrder(OpenOrder(patient.Id));

        var discharge = patient.Discharge(
            Guid.NewGuid(),
            DischargeType.AtPatientRequest,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "lekarz.emg",
            department: null,
            reason: "  Pacjent nie wyraża zgody na dalsze leczenie  ");

        Assert.Equal(PatientState.Closed, patient.State);
        Assert.Equal("Pacjent nie wyraża zgody na dalsze leczenie", discharge.Reason);
        Assert.Equal(MedicalOrderState.Cancelled, order.State);
    }

    [Fact]
    public void PrzekazanieNaOddzial_BezOddzialu_JestZablokowane()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("J18.9"));

        var blockers = patient.GetDischargeBlockers(
            DischargeType.TransferToDepartment,
            hasTargetDepartment: false,
            hasReason: true);

        Assert.NotEmpty(blockers);
    }

    [Fact]
    public void PrzekazanieNaOddzial_BezPowodu_JestZablokowane()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("J18.9"));

        var blockers = patient.GetDischargeBlockers(
            DischargeType.TransferToDepartment,
            hasTargetDepartment: true,
            hasReason: false);

        Assert.NotEmpty(blockers);
    }

    [Fact]
    public void PrzekazanieNaOddzial_UstawiaStanPrzekazanyIZapisujeOddzial()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("J18.9"));

        var department = Department.Create(Guid.NewGuid(), "NEF", "Nefrologia");

        var discharge = patient.Discharge(
            Guid.NewGuid(),
            DischargeType.TransferToDepartment,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "lekarz.emg",
            department,
            "Konieczna dializoterapia");

        Assert.Equal(PatientState.TransferredOut, patient.State);
        Assert.Equal(department.Id, discharge.DepartmentId);
        Assert.Equal("Nefrologia", discharge.DepartmentName);
    }

    [Fact]
    public void PonownyWypis_JestZablokowany()
    {
        var patient = PatientInTreatment();
        patient.SetDiagnosis(Icd10Code.Create("I21.4"));
        patient.Discharge(
            Guid.NewGuid(),
            DischargeType.TreatmentCompleted,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "lekarz.emg");

        var blockers = patient.GetDischargeBlockers(DischargeType.AtPatientRequest, hasReason: true);

        Assert.NotEmpty(blockers);
    }

    // -----------------------------------------------------------------------
    // Serwis pacjenta i katalog oddziałów
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LekarzWypisujePacjentaPoZakonczeniuLeczenia()
    {
        var patientId = await CreateTreatedPatientAsync();

        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        await _patients.SetDiagnosisAsync(patientId, "I21.4");

        var details = await _patients.DischargePatientAsync(patientId, DischargeType.TreatmentCompleted);

        Assert.Equal(PatientState.Closed, details.State);
        var discharge = Assert.Single(details.Discharges);
        Assert.Equal(DischargeType.TreatmentCompleted, discharge.Type);
        Assert.Equal("lekarz.emg", discharge.RecordedByLogin);
        Assert.Empty(details.DischargeBlockers);

        var audit = await _context.AuditLogEntries.SingleAsync(entry => entry.EntityId == patientId);
        Assert.Equal(AuditActionType.PatientDischarged, audit.ActionType);
        Assert.True(audit.Success);
    }

    [Fact]
    public async Task WypisZOtwartymZleceniem_JestOdrzuconyIAudytowany()
    {
        var patientId = await CreateTreatedPatientAsync();

        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        await _patients.SetDiagnosisAsync(patientId, "I21.4");
        await _patients.AddOrderAsync(patientId, MedicalOrderType.Lab, "Morfologia przed wypisem", isUrgent: false);

        var exception = await Assert.ThrowsAsync<PatientDischargeBlockedException>(() =>
            _patients.DischargePatientAsync(patientId, DischargeType.TreatmentCompleted));

        Assert.NotEmpty(exception.Reasons);

        var details = await _patients.GetPatientAsync(patientId);
        Assert.Equal(PatientState.InTreatment, details.State);

        var audit = await _context.AuditLogEntries.SingleAsync(entry => entry.EntityId == patientId);
        Assert.Equal(AuditActionType.PatientDischargeBlocked, audit.ActionType);
        Assert.False(audit.Success);
    }

    [Fact]
    public async Task WypisNaWlasneZadanie_WymagaPowodu()
    {
        var patientId = await CreateTreatedPatientAsync();

        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");

        await Assert.ThrowsAsync<PatientDischargeBlockedException>(() =>
            _patients.DischargePatientAsync(patientId, DischargeType.AtPatientRequest));
    }

    [Fact]
    public async Task PrzekazanieNaOddzial_WymagaIstniejacegoOddzialu()
    {
        var patientId = await CreateTreatedPatientAsync();

        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        await _patients.SetDiagnosisAsync(patientId, "J18.9");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _patients.DischargePatientAsync(
                patientId,
                DischargeType.TransferToDepartment,
                Guid.NewGuid(),
                "Przekazanie do oddziału"));

        var departments = await _departments.GetAllAsync();
        var nefrologia = departments.Single(department => department.Code == "NEF");

        var details = await _patients.DischargePatientAsync(
            patientId,
            DischargeType.TransferToDepartment,
            nefrologia.Id,
            "Konieczna dializoterapia");

        Assert.Equal(PatientState.TransferredOut, details.State);
        Assert.Equal(nefrologia.Name, details.Discharges.Single().DepartmentName);
    }

    [Fact]
    public async Task KatalogOddzialowZawieraOddzialySzpitala()
    {
        var departments = await _departments.GetAllAsync();

        Assert.NotEmpty(departments);
        Assert.Equal(
            departments.Select(department => department.Code).Distinct().Count(),
            departments.Count);
        Assert.Contains(departments, department => department.Code == "SOR" && department.Name.Contains("Medycyny Ratunkowej", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(departments, department => department.Code == "NEF");
        Assert.Contains(departments, department => department.Code == "PHO");
    }

    // -----------------------------------------------------------------------
    // Pomocnicze
    // -----------------------------------------------------------------------

    private static Patient PatientInTreatment()
    {
        var patient = Patient.Register(
            Guid.NewGuid(),
            "85441410008",
            "Anna",
            "Kowalska",
            new DateOnly(1985, 4, 14),
            PatientGender.Male,
            null,
            DateTimeOffset.UtcNow);

        patient.AssignTriage(TriageAssessment.Create(
            Guid.NewGuid(),
            patient.Id,
            Guid.NewGuid(),
            TriageCategory.Yellow,
            "Pacjent wymaga dalszej diagnostyki",
            "RR 120/80, HR 88",
            DateTimeOffset.UtcNow));

        patient.AssignToZone(Guid.NewGuid(), DateTimeOffset.UtcNow);

        return patient;
    }

    private static MedicalOrder OpenOrder(Guid patientId) => MedicalOrder.Create(
        Guid.NewGuid(),
        patientId,
        Guid.NewGuid(),
        UserRole.Physician,
        MedicalOrderType.Lab,
        "Morfologia krwi",
        DateTimeOffset.UtcNow);

    private static MedicalOrder CompletedOrder(Guid patientId)
    {
        var order = OpenOrder(patientId);

        order.ChangeState(MedicalOrderState.Completed, DateTimeOffset.UtcNow);

        return order;
    }

    /// <summary>Pacjent w strefie lekarza SOR, gotowy do decyzji o wypisie.</summary>
    private async Task<Guid> CreateTreatedPatientAsync()
    {
        await _auth.LoginAsync("lekarz.emg", "SOR2026!emg");

        var cards = await _patients.GetZonePatientsAsync(_auth.CurrentUser!.CurrentZoneId);

        return cards.First(card => card.State == PatientState.InTreatment).Id;
    }
}