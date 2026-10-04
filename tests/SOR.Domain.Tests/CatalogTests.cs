using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;
using SOR.Infrastructure;
using SOR.Infrastructure.Persistence;
using Xunit;

namespace SOR.Domain.Tests;

/// <summary>
/// Testy walidacji numeru PESEL (BR-18) — suma kontrolna, zakodowana data i płeć.
/// </summary>
public sealed class PeselNumberTests
{
    [Theory]
    [InlineData("85441410008", 1985, 4, 14, PeselNumber.GenderEncoded.Male)]
    [InlineData("90311210011", 1990, 11, 12, PeselNumber.GenderEncoded.Female)]
    [InlineData("72430810026", 1972, 3, 8, PeselNumber.GenderEncoded.Male)]
    [InlineData("34210610077", 1934, 1, 6, PeselNumber.GenderEncoded.Female)]
    [InlineData("11310510128", 2011, 11, 5, PeselNumber.GenderEncoded.Male)]
    [InlineData("04030712354", 2004, 3, 7, PeselNumber.GenderEncoded.Female)]
    [InlineData("25262112349", 2025, 6, 21, PeselNumber.GenderEncoded.Male)]
    public void PoprawnyPesel_OdczytujeDateIGene(
        string pesel,
        int year,
        int month,
        int day,
        PeselNumber.GenderEncoded gender)
    {
        var parsed = PeselNumber.Create(pesel);

        Assert.Equal(pesel, parsed.Value);
        Assert.Equal(new DateOnly(year, month, day), parsed.DateOfBirth);
        Assert.Equal(gender, parsed.Gender);
    }

    [Fact]
    public void NiepoprawnaSumaKontrolna_JestOdrzucona()
    {
        // Cyfra kontrolna zmieniona z 8 na 2 — numer nie istnieje.
        Assert.False(PeselNumber.TryParse("85441410002", out _));

        var exception = Assert.Throws<ValidationException>(() => PeselNumber.Create("85441410002"));
        Assert.Contains("sumy kontrolnej", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8544141000")]        // 10 znaków
    [InlineData("854414100081")]      // 12 znaków
    [InlineData("8544141000X")]       // litera w numerze
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NumerNiepelnejDlugosci_LubZNieCyfr_JestOdrzucony(string? raw)
    {
        Assert.False(PeselNumber.TryParse(raw, out var pesel));
        Assert.Null(pesel);
    }

    [Fact]
    public void SeparatoryWNumerzeSaIgnorowane()
    {
        Assert.True(PeselNumber.TryParse("854 414 10008", out var withSpaces));
        Assert.True(PeselNumber.TryParse("854-414-10008", out var withDashes));
        Assert.True(PeselNumber.TryParse("854.414.10008", out var withDots));

        Assert.Equal("85441410008", withSpaces!.Value);
        Assert.Equal(withSpaces, withDashes);
        Assert.Equal(withSpaces, withDots);
    }

    [Fact]
    public void NieistniejacaData_Urodzin_JestOdrzucona()
    {
        // 30 lutego 1985 oraz 31 kwietnia 1985 — daty, które nie istnieją.
        // W obu numerach suma kontrolna jest poprawna, więc odrzucenie wynika z daty.
        Assert.False(PeselNumber.TryParse("85023012347", out _));
        Assert.False(PeselNumber.TryParse("85043112346", out _));
    }

    [Fact]
    public void NiepoprawnyMiesiac_Zkodowania_JestOdrzucony()
    {
        // Miesiąc 13 — poza dopuszczalnym zakresem 01–12, 21–32, 41–52, 61–72, 81–92.
        Assert.False(PeselNumber.TryParse("85134500000", out _));
    }

    [Fact]
    public void NumerZeStuleciaPoprzedniego_JestObslugiwany()
    {
        // Kobieta urodzona w 1934 r. — miesiąc zakodowany z przesunięciem +20 (XX wiek).
        var pesel = PeselNumber.Create("34210610077");

        Assert.Equal(1934, pesel.DateOfBirth.Year);
        Assert.Equal(PeselNumber.GenderEncoded.Female, pesel.Gender);
    }

    [Theory]
    [InlineData("85441410008", 1985, 4, 14, PatientGender.Male)]
    [InlineData("90311210011", 1990, 11, 12, PatientGender.Female)]
    [InlineData("25262112349", 2025, 6, 21, PatientGender.Male)]
    public void TryDecode_WyliczaDateUrDZINaIPlecZNumeru(
        string raw,
        int year,
        int month,
        int day,
        PatientGender gender)
    {
        Assert.True(PeselNumber.TryDecode(raw, out var dateOfBirth, out var decodedGender));

        Assert.Equal(new DateOnly(year, month, day), dateOfBirth);
        Assert.Equal(gender, decodedGender);
    }

    [Theory]
    [InlineData("8544141000")]     // za krótki
    [InlineData("854414100080")]   // za długi
    [InlineData("85441410002")]    // zła suma kontrolna
    [InlineData("85023012347")]    // 30 lutego nie istnieje
    [InlineData("")]
    [InlineData(null)]
    public void TryDecode_OdrzucaNumerNiepoprawny(string? raw)
    {
        Assert.False(PeselNumber.TryDecode(raw, out _, out _));
    }

    [Fact]
    public void MatchesGender_RozpoznajeZgodnaZNiezgodnaPlec()
    {
        var pesel = PeselNumber.Create("85441410008");

        Assert.True(pesel.MatchesGender(PatientGender.Male));
        Assert.False(pesel.MatchesGender(PatientGender.Female));

        // Numer rozróżnia tylko płeć żeńską i męską, więc "Other" nie pasuje do żadnego PESEL.
        Assert.False(pesel.MatchesGender(PatientGender.Other));
    }

    [Fact]
    public void RejestracjaPacjenta_OdrzucaPlecInnaNiżZakodowanaWNumerze()
    {
        // PESEL 90311210011 koduje płeć żeńską, a w formularzu wybrano "Inna".
        var exception = Assert.Throws<ValidationException>(() => Patient.Register(
            Guid.NewGuid(),
            "90311210011",
            "Anna",
            "Nowak",
            new DateOnly(1990, 11, 12),
            PatientGender.Other,
            null,
            DateTimeOffset.UtcNow));

        Assert.Contains("BR-18", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejestracjaPacjenta_OdrzucaPeselNiezgodnyZDatąUrDZIN()
    {
        // PESEL koduje 1985-04-14, a w formularzu podano 1990-11-12.
        var exception = Assert.Throws<ValidationException>(() => Patient.Register(
            Guid.NewGuid(),
            "85441410008",
            "Jan",
            "Kowalski",
            new DateOnly(1990, 11, 12),
            PatientGender.Male,
            "Ból brzucha",
            DateTimeOffset.UtcNow));

        Assert.Contains("nie zgadza się", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejestracjaPacjenta_OdrzucaPeselNiezgodnyZPłcią()
    {
        // PESEL 85441410008 koduje płeć męską, a w formularzu podano płeć żeńską.
        var exception = Assert.Throws<ValidationException>(() => Patient.Register(
            Guid.NewGuid(),
            "85441410008",
            "Jan",
            "Kowalski",
            new DateOnly(1985, 4, 14),
            PatientGender.Female,
            "Ból brzucha",
            DateTimeOffset.UtcNow));

        Assert.Contains("BR-18", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejestracjaPacjenta_AkceptujeSpójneDane()
    {
        var pesel = "90311210011";

        var patient = Patient.Register(
            Guid.NewGuid(),
            pesel,
            "Anna",
            "Nowak",
            new DateOnly(1990, 11, 12),
            PatientGender.Female,
            "Ból w klatce piersiowej",
            DateTimeOffset.UtcNow);

        Assert.Equal(pesel, patient.Pesel);
        Assert.Equal(new DateOnly(1990, 11, 12), patient.DateOfBirth);
        Assert.Equal(PatientGender.Female, patient.Gender);
        Assert.Equal(PatientState.Registered, patient.State);
    }

    [Fact]
    public void RejestracjaPacjenta_AkceptujeKobietęUrodzonąWXXIWieku()
    {
        // Miesiąc 03 bez przesunięcia oznacza kobiety z XXI wieku; płeć koduje
        // nieparzysta dziesiąta cyfra numeru seryjnego, a nie przesunięcie miesiąca.
        var pesel = "04030712354";

        var patient = Patient.Register(
            Guid.NewGuid(),
            pesel,
            "Zuzanna",
            "Kowalczyk",
            new DateOnly(2004, 3, 7),
            PatientGender.Female,
            "Uraz kończyny",
            DateTimeOffset.UtcNow);

        Assert.Equal(PatientGender.Female, patient.Gender);
        Assert.Equal(new DateOnly(2004, 3, 7), patient.DateOfBirth);
    }

    [Fact]
    public void RejestracjaPacjenta_AkceptujeKobietęUrodzonąWXXWieku()
    {
        // Ten sam zapis 25|26|21 oznacza kobietę urodzoną w 1925 r. (miesiąc +20)
        // albo mężczyznę urodzonego w 2025 r. — stulecie rozstrzyga dopasowanie
        // do daty zadeklarowanej w formularzu, a płeć — parzystość dziesiątej cyfry.
        var pesel = "25262112351";

        var patient = Patient.Register(
            Guid.NewGuid(),
            pesel,
            "Helena",
            "Zielińska",
            new DateOnly(1925, 6, 21),
            PatientGender.Female,
            "Zawroty głowy",
            DateTimeOffset.UtcNow);

        Assert.Equal(PatientGender.Female, patient.Gender);
        Assert.Equal(new DateOnly(1925, 6, 21), patient.DateOfBirth);
    }

    [Fact]
    public void RejestracjaPacjenta_AkceptujeMężczyznęUrodzonegoWXXIWieku()
    {
        // Mężczyzna urodzony w 2025 r. zapisuje miesiąc z przesunięciem +20, czyli
        // identycznie jak kobieta urodzona w 1925 r. Rozróżnia ich parzystość dziesiątej cyfry.
        var pesel = "25262112349";

        var patient = Patient.Register(
            Guid.NewGuid(),
            pesel,
            "Adam",
            "Wiśniewski",
            new DateOnly(2025, 6, 21),
            PatientGender.Male,
            "Ból brzucha",
            DateTimeOffset.UtcNow);

        Assert.Equal(PatientGender.Male, patient.Gender);
        Assert.Equal(new DateOnly(2025, 6, 21), patient.DateOfBirth);
    }

    [Fact]
    public void RejestracjaPacjenta_OdrzucaRozbieżnyMiesiącDlaTegoSamegoZapisu()
    {
        // Ten sam zapis co w teście kobiety z 1925 r., ale w formularzu podano lipiec.
        var exception = Assert.Throws<ValidationException>(() => Patient.Register(
            Guid.NewGuid(),
            "25262112351",
            "Helena",
            "Zielińska",
            new DateOnly(1925, 7, 21),
            PatientGender.Female,
            "Zawroty głowy",
            DateTimeOffset.UtcNow));

        Assert.Contains("nie zgadza się", exception.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Testy reguł dziedzinowych katalogu leków, pakietów medycznych i kodów ICD-10.
/// </summary>
public sealed class CatalogDomainTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Lek_WymagaKoduNazwyIDawki()
    {
        Assert.Throws<ValidationException>(() => Medication.Create(
            Guid.Empty,
            "LEK-001",
            "Adrenalina",
            "ampułka",
            "1 mg/ml",
            MedicationCategory.Cardiovascular,
            MedicationRoute.Intravenous,
            "1 mg i.v.",
            "4 mg/dobę"));

        Assert.Throws<ValidationException>(() => Medication.Create(
            Guid.NewGuid(),
            "L",
            "Adrenalina",
            "ampułka",
            "1 mg/ml",
            MedicationCategory.Cardiovascular,
            MedicationRoute.Intravenous,
            "1 mg i.v.",
            "4 mg/dobę"));

        Assert.Throws<ValidationException>(() => Medication.Create(
            Guid.NewGuid(),
            "LEK-001",
            "Adrenalina",
            "ampułka",
            "1 mg/ml",
            MedicationCategory.Cardiovascular,
            MedicationRoute.Intravenous,
            "  ",
            "4 mg/dobę"));
    }

    [Fact]
    public void Lek_NormalizujeKodIOpisWyświetlany()
    {
        var medication = Medication.Create(
            Id,
            "lek-emg-0012",
            "  Tiotropium  ",
            "roztwór nebulizacyjny",
            "0,5 mg/ml",
            MedicationCategory.Neurologic,
            MedicationRoute.Inhalation,
            " " + "2,5 mg nebulizacja",
            " " + "5 mg/dobę",
            MedicationSafety.HighAlert,
            contraindications: "  jaskra  ",
            notes: "   ");

        Assert.Equal("LEK-EMG-0012", medication.Code);
        Assert.Equal("Tiotropium", medication.Name);
        Assert.Equal("jaskra", medication.Contraindications);
        Assert.Null(medication.Notes);
        Assert.Contains("Tiotropium 0,5 mg/ml", medication.DisplayName, StringComparison.Ordinal);
        Assert.True(medication.IsAvailable);
        Assert.Equal(MedicationSafety.HighAlert, medication.Safety);
    }

    [Fact]
    public void Lek_MoznaWycofacIZPowrotemWlaczycBezUsuwaniaHistorii()
    {
        var medication = Medication.Create(
            Id,
            "LEK-EMG-0100",
            "Trandolapril",
            "tabletki",
            "2 mg",
            MedicationCategory.Cardiovascular,
            MedicationRoute.Oral,
            "2 mg p.o.",
            "8 mg/dobę");

        medication.Deactivate();
        Assert.False(medication.IsAvailable);

        medication.Activate();
        Assert.True(medication.IsAvailable);
    }

    [Fact]
    public void Pakiet_NiePrzyjmujePowtorzonejPozycji()
    {
        var bundle = MedicalBundle.Create(
            Id,
            "pak-sor-001",
            "Ból brzucha — diagnostyka i leczenie",
            "Ostry ból brzucha",
            "Bóle brzucha");

        bundle.AddItem(Guid.NewGuid(), 1, MedicalOrderType.Lab, "Morfologia krwi", isUrgent: true);
        bundle.AddItem(Guid.NewGuid(), 2, MedicalOrderType.Procedure, "USG jamy brzusznej");

        Assert.Equal(2, bundle.Items.Count);

        Assert.Throws<ValidationException>(() =>
            bundle.AddItem(Guid.NewGuid(), 1, MedicalOrderType.Lab, "Duplikat pozycji"));
    }

    [Fact]
    public void Pakiet_NiePrzyjmujePustegoWskazania()
    {
        Assert.Throws<ValidationException>(() => MedicalBundle.Create(
            Guid.NewGuid(),
            "PAK-SOR-002",
            "Zatrudnienie",
            "  ",
            "Zatrucia"));
    }

    [Fact]
    public void PozycjaPakietu_WymagaOpisu()
    {
        var bundle = MedicalBundle.Create(
            Id,
            "PAK-SOR-003",
            "Wstrząs",
            "Wstrząs krwotoczny",
            "Wstrząs");

        var item = bundle.AddItem(
            Guid.NewGuid(),
            1,
            MedicalOrderType.Medication,
            "Adrenalina 1 mg i.v.",
            MedicationRoute.Intravenous,
            Guid.NewGuid(),
            "1 mg",
            isUrgent: true);

        Assert.Equal(1, item.Sequence);
        Assert.Equal(MedicalOrderType.Medication, item.OrderType);
        Assert.True(item.IsUrgent);
        Assert.Equal(MedicationRoute.Intravenous, item.Route);
        Assert.Equal(bundle.Id, item.BundleId);

        // Pozycja bez opisu jest odrzucana — opis jest tym, co zostanie zlecone.
        var other = MedicalBundle.Create(
            Guid.NewGuid(),
            "PAK-SOR-004",
            "Zatrucie",
            "Zatrucie lekami",
            "Zatrucia");

        Assert.Throws<ValidationException>(() => other.AddItem(
            Guid.NewGuid(),
            1,
            MedicalOrderType.Medication,
            "   ",
            MedicationRoute.Oral,
            Guid.NewGuid(),
            "1 tabletka"));
    }

    [Fact]
    public void KodIcd10_WymagaKoduOpisuIRozdziału()
    {
        Assert.Throws<ValidationException>(() => Icd10CatalogEntry.Create(
            Guid.NewGuid(),
            " ",
            "Ostre niedokrwienie serca",
            "IX. Choroby układu krążenia"));

        var entry = Icd10CatalogEntry.Create(
            Id,
            "i21.4",
            "Ostre niedokrwienie serca, nieokreślone",
            "IX. Choroby układu krążenia",
            "Zawały serca",
            isEmergencyRelevant: true);

        Assert.Equal("I21.4", entry.Code);
        Assert.True(entry.IsEmergencyRelevant);
        Assert.Equal("Zawały serca", entry.Category);
    }

    [Fact]
    public void PodanieLeku_WymagaDawkiIDaty()
    {
        Assert.Throws<ValidationException>(() => MedicationAdministration.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  ",
            MedicationRoute.Intravenous,
            DateTimeOffset.UtcNow));

        Assert.Throws<ValidationException>(() => MedicationAdministration.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1 mg",
            MedicationRoute.Intravenous,
            default));

        Assert.Throws<ValidationException>(() => MedicationAdministration.Record(
            Guid.NewGuid(),
            Guid.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1 mg",
            MedicationRoute.Intravenous,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PodanieLeku_TrafilDoHistoriiPacjenta()
    {
        var patient = Patient.Register(
            Guid.NewGuid(),
            "85441410008",
            "Marek",
            "Zieliński",
            new DateOnly(1985, 4, 14),
            PatientGender.Male,
            null,
            DateTimeOffset.UtcNow);

        var administration = patient.RecordAdministration(MedicationAdministration.Record(
            Guid.NewGuid(),
            patient.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1 mg i.v.",
            MedicationRoute.Intravenous,
            DateTimeOffset.UtcNow));

        Assert.Same(administration, Assert.Single(patient.Administrations));
    }

    [Fact]
    public void PodanieLeku_NiedozwolonePoWypisiePacjenta()
    {
        var patient = Patient.Register(
            Guid.NewGuid(),
            "85441410008",
            "Marek",
            "Zieliński",
            new DateOnly(1985, 4, 14),
            PatientGender.Male,
            null,
            DateTimeOffset.UtcNow);

        patient.SetDiagnosis(Icd10Code.Create("I21.4"));
        patient.AssignTriage(TriageAssessment.Create(
            Guid.NewGuid(),
            patient.Id,
            Guid.NewGuid(),
            TriageCategory.Red,
            "Bólu klatki piersiowej",
            "RR 150/90, HR 110",
            DateTimeOffset.UtcNow));
        patient.AssignToZone(Guid.NewGuid(), DateTimeOffset.UtcNow);
        patient.Discharge(
            Guid.NewGuid(),
            DischargeType.TreatmentCompleted,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "lekarz",
            department: null,
            reason: null);
        Assert.Equal(PatientState.Closed, patient.State);

        Assert.Throws<ValidationException>(() => patient.RecordAdministration(
            MedicationAdministration.Record(
                Guid.NewGuid(),
                patient.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "1 mg i.v.",
                MedicationRoute.Intravenous,
                DateTimeOffset.UtcNow)));
    }
}

/// <summary>
/// Testy integracyjne katalogów referencyjnych i obsługi podanych leków: seeding formularza,
/// wyszukiwanie, walidacja kodu ICD-10 oraz rejestr podanych leków.
/// </summary>
public sealed class CatalogWorkflowTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sor-catalog-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSorSystem(_databasePath);

        await using var provider = services.BuildServiceProvider();
        await provider.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Pliki dziennika WAL i indeksu SQLite musi zniknąć razem z bazą, inaczej
        // zostają w katalogu tymczasowym między uruchomieniami testów.
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
                    // Połączenie może jeszcze zamykać się w tle — ponawiamy próbę.
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
    public async Task KatalogiReferencyjneSezowanePoprawnie()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();

        var medications = await context.Medications.ToListAsync();
        var icd10Entries = await context.Icd10CatalogEntries.ToListAsync();
        var bundles = await context.MedicalBundles
            .Include(bundle => bundle.Items)
            .ToListAsync();

        Assert.True(medications.Count >= 20, $"Katalog leków: {medications.Count}");
        Assert.True(icd10Entries.Count >= 20, $"Katalog ICD-10: {icd10Entries.Count}");
        Assert.True(bundles.Count >= 3, $"Pakiety medyczne: {bundles.Count}");

        // Kody muszą być unikalne — katalog jest zbiorem referencyjnym adresowanym po kodzie.
        Assert.Equal(medications.Count, medications.Select(m => m.Code).Distinct().Count());
        Assert.Equal(icd10Entries.Count, icd10Entries.Select(e => e.Code).Distinct().Count());

        // Każdy pakiet musi wskazywać leki, które istnieją w formularzu.
        var medicationIds = medications.Select(m => m.Id).ToHashSet();

        foreach (var bundle in bundles)
        {
            Assert.NotEmpty(bundle.Items);

            foreach (var item in bundle.Items)
            {
                if (item.MedicationId is { } medicationId)
                {
                    Assert.Contains(medicationId, medicationIds);
                }
            }
        }
    }

    [Fact]
    public async Task WyszukiwanieLeku_ZwracaTylkoPasujacePozycjeKatalogu()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IMedicationCatalogService>();

        var all = await catalog.SearchAsync(null);
        Assert.NotEmpty(all);

        var filtered = await catalog.SearchAsync("adrenalina");
        Assert.NotEmpty(filtered);
        Assert.All(filtered, m => Assert.Contains("adrenalina", m.Name, StringComparison.OrdinalIgnoreCase));

        var byCode = await catalog.SearchAsync(all[0].Code);
        Assert.Single(byCode);
        Assert.Equal(all[0].Id, byCode[0].Id);

        var highAlert = await catalog.GetHighAlertAsync();
        Assert.NotEmpty(highAlert);
        Assert.All(highAlert, m => Assert.True(m.RequiresCloseMonitoring));

        var categories = await catalog.GetCategoriesAsync();
        Assert.NotEmpty(categories);
        Assert.Equal(categories.Sum(c => c.Count), all.Count);
    }

    [Fact]
    public async Task RozpoznanieIcd10_WeryfikujeKodWzgledemKatalogu()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientService>();
        var icd10 = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IIcd10CatalogService>();

        var session = await auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        Assert.NotEqual(Guid.Empty, session.User.CurrentZoneId);

        var patient = await LoadZonePatientAsync(scope);
        var code = (await icd10.GetEmergencyRelevantAsync())[0];

        var assigned = await icd10.AssignAsync(patient.Id, code.Code);
        Assert.Equal(code.Code, assigned.Diagnosis);

        var cleared = await icd10.AssignAsync(patient.Id, null);
        Assert.Null(cleared.Diagnosis);

        await Assert.ThrowsAnyAsync<Exception>(() => icd10.AssignAsync(patient.Id, "X00.0-nieistniejacy"));

        _ = patients;
    }

    [Fact]
    public async Task PodanieLeku_TrafiaDoRejestruPodanychLekow()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientService>();
        var catalog = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IMedicationCatalogService>();

        var session = await auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        Assert.NotEqual(Guid.Empty, session.User.CurrentZoneId);

        var patient = await LoadZonePatientAsync(scope);
        var medication = (await catalog.SearchAsync(null)).First(m => m.IsAvailable);

        var details = await patients.RecordMedicationAdministrationAsync(
            patient.Id,
            medication.Id,
            medication.TypicalDose,
            medication.Route);

        var administration = Assert.Single(details.Administrations);
        Assert.Equal(medication.Id, administration.MedicationId);
        Assert.Equal(medication.TypicalDose, administration.Dose);
        Assert.Equal(medication.Route, administration.Route);
    }

    [Fact]
    public async Task DawkaNiezgodnaZKatalogiem_JestOdrzucona()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientService>();
        var catalog = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IMedicationCatalogService>();

        var session = await auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        Assert.NotEqual(Guid.Empty, session.User.CurrentZoneId);

        var patient = await LoadZonePatientAsync(scope);
        var medication = (await catalog.SearchAsync(null)).First(m => m.IsAvailable);

        await Assert.ThrowsAnyAsync<Exception>(() => patients.RecordMedicationAdministrationAsync(
            patient.Id,
            medication.Id,
            "dawka spoza katalogu",
            medication.Route));
    }

    [Fact]
    public async Task ZastosowaniePakietu_TworzyZleceniaIRejestrPodanychLekow()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientService>();
        var bundles = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IMedicalBundleService>();

        var session = await auth.LoginAsync("lekarz.emg", "SOR2026!emg");
        Assert.NotEqual(Guid.Empty, session.User.CurrentZoneId);

        var patient = await LoadZonePatientAsync(scope);
        var ordersBefore = patient.Orders.Count;

        var bundle = Assert.Single(await bundles.SearchAsync("PAK-SOR-007"));
        Assert.Contains(bundle.Items, i => i.OrderType == MedicalOrderType.Medication);

        var details = await bundles.ApplyAsync(patient.Id, bundle.Id);

        // Kroki obserwacyjne nie generują zlecenia — jedynie notatki postępowania.
        Assert.Equal(ordersBefore + bundle.OrderCount, details.Orders.Count);
        Assert.All(
            bundle.Items.Where(item => item.OrderType != MedicalOrderType.Observation),
            item => Assert.Contains(details.Orders, o => o.Description == item.Description));

        // Pozycje farmakologiczne pakietu są odnotowane jako faktycznie podane.
        var medicationItems = bundle.Items.Count(i => i.OrderType == MedicalOrderType.Medication);
        Assert.Equal(medicationItems, details.Administrations.Count);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSorSystem(_databasePath);
        return services.BuildServiceProvider();
    }

    /// <summary>Zwraca pacjenta przypisanego do strefy zalogowanego lekarza.</summary>
    private static async Task<Application.DTOs.PatientDetailsDto> LoadZonePatientAsync(IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<SorDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IAuthenticationService>();
        var patients = scope.ServiceProvider.GetRequiredService<Application.Interfaces.IPatientService>();

        var zoneId = auth.CurrentUser!.CurrentZoneId;
        Assert.NotEqual(Guid.Empty, zoneId);

        var patientId = await context.Patients
            .Where(p => p.ZoneId == zoneId && p.State != PatientState.Closed)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(patientId);
        return await patients.GetPatientAsync(patientId!.Value);
    }
}