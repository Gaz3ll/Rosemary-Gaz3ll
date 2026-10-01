using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;
using SOR.Domain.ValueObjects;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Generator danych startowych (seed). Tworzy kompletny, realistyczny stan oddziału:
/// 4 strefy, personel z rolami, aktywny grafik dyżuru oraz pacjentów o zróżnicowanym
/// kodzie Triage, aby od razu po uruchomieniu widoczne były różne statusy obciążenia stref.
/// Dane startowe ułatwiają prezentację systemu podczas obrony projektu.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly SorDbContext _context;
    private readonly IIdGenerator _idGenerator;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        SorDbContext context,
        IIdGenerator idGenerator,
        IClock clock,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Wypełnia bazę danymi startowymi, jeśli nie zawiera jeszcze żadnych stref.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _context.Zones.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Baza zawiera już dane — pomijam generowanie danych startowych.");
            return;
        }

        _logger.LogInformation("Generuję dane startowe systemu SOR...");

        var now = _clock.UtcNow;

        var triage = Zone.Create(_idGenerator.NewId(), "TRI", "Moduł wstępny — Triage", ZoneKind.Triage, 12);
        var emergency = Zone.Create(_idGenerator.NewId(), "EMG", "Część ratunkowa (Trauma Room)", ZoneKind.Emergency, 6);
        var internalZone = Zone.Create(_idGenerator.NewId(), "INT", "Część internistyczna", ZoneKind.Internal, 14);
        var trauma = Zone.Create(_idGenerator.NewId(), "TRM", "Część urazowo-ortopedyczna", ZoneKind.Trauma, 8);

        _context.Zones.AddRange(triage, emergency, internalZone, trauma);

        // ---------- Personel ----------
        var coordinator = User.Create(_idGenerator.NewId(), "ordynator", "dr n. med. Anna Kowalska", "SOR2026!ord", UserRole.Coordinator, "PWZ-100234");
        var emergencyDoctor = User.Create(_idGenerator.NewId(), "lekarz.emg", "dr Piotr Nowak", "SOR2026!emg", UserRole.Physician, "PWZ-200345");
        var traumaDoctor = User.Create(_idGenerator.NewId(), "lekarz.trm", "dr Maria Wiśniewska", "SOR2026!trm", UserRole.Physician, "PWZ-300456");
        var internalDoctor = User.Create(_idGenerator.NewId(), "lekarz.int", "dr Tomasz Lewandowski", "SOR2026!int", UserRole.Physician, "PWZ-400567");
        var nurse = User.Create(_idGenerator.NewId(), "piel.triage", "mgr Ewa Zielińska", "SOR2026!tri", UserRole.Nurse, "PWZ-500678");
        var nurseTrauma = User.Create(_idGenerator.NewId(), "piel.trm", "mgr Krzysztof Szymański", "SOR2026!pt", UserRole.Nurse, "PWZ-600789");

        _context.Users.AddRange(coordinator, emergencyDoctor, traumaDoctor, internalDoctor, nurse, nurseTrauma);

        // ---------- Grafik dyżuru (aktywny w chwili uruchomienia) ----------
        var shiftStart = now.AddHours(-2);
        var shiftEnd = now.AddHours(8);

        _context.DutyShifts.AddRange(
            DutyShift.Create(_idGenerator.NewId(), emergencyDoctor.Id, emergency.Id, shiftStart, shiftEnd, "Dyżur główny — część ratunkowa"),
            DutyShift.Create(_idGenerator.NewId(), traumaDoctor.Id, trauma.Id, shiftStart, shiftEnd, "Dyżur — część urazowo-ortopedyczna"),
            DutyShift.Create(_idGenerator.NewId(), internalDoctor.Id, internalZone.Id, shiftStart, shiftEnd, "Dyżur — część internistyczna"),
            DutyShift.Create(_idGenerator.NewId(), nurse.Id, triage.Id, shiftStart, shiftEnd, "Triage — zmiana poranna"),
            DutyShift.Create(_idGenerator.NewId(), coordinator.Id, emergency.Id, shiftStart, shiftEnd, "Koordynacja oddziału"),
            DutyShift.Create(_idGenerator.NewId(), nurseTrauma.Id, triage.Id, shiftStart, shiftEnd, "Wsparcie triage"));

        // ---------- Przypisania personelu do stref ----------
        _context.StaffZoneAssignments.AddRange(
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), emergencyDoctor.Id, emergency.Id, shiftStart),
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), traumaDoctor.Id, trauma.Id, shiftStart),
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), internalDoctor.Id, internalZone.Id, shiftStart),
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), nurse.Id, triage.Id, shiftStart),
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), coordinator.Id, emergency.Id, shiftStart),
            StaffZoneAssignment.FromRoster(_idGenerator.NewId(), nurseTrauma.Id, triage.Id, shiftStart));

        // ---------- Pacjenci ----------
        SeedPatients(now, emergency, internalZone, trauma, emergencyDoctor, traumaDoctor, internalDoctor, nurse);

        _context.AuditLogEntries.Add(AuditLogEntry.Record(
            _idGenerator.NewId(),
            AuditActionType.LoginAttempt,
            null,
            "SYSTEM",
            null,
            nameof(Zone),
            $"Wygenerowano dane startowe systemu: strefy {emergency.Code}, {internalZone.Code}, {trauma.Code}, {triage.Code}.",
            now));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Dane startowe wygenerowane pomyślnie.");
    }

    private void SeedPatients(
        DateTimeOffset now,
        Zone emergency,
        Zone internalZone,
        Zone trauma,
        User emergencyDoctor,
        User traumaDoctor,
        User internalDoctor,
        User nurse)
    {
        // Pacjenci z założonym rozkładem kodów Triage — część ratunkowa celowo przeciążona.
        AddPatient("85041400844", "Marek", "Zieliński", new DateOnly(1985, 4, 14), PatientGender.Male,
            "Wypadek komunikacyjny, krwawienie wewnętrzne", TriageCategory.Red, emergency, emergencyDoctor, 40, now,
            "S72.1");

        AddPatient("90111200315", "Katarzyna", "Wójcik", new DateOnly(1990, 11, 12), PatientGender.Female,
            "Ból w klatce piersiowej, podejrzenie OZW", TriageCategory.Orange, emergency, emergencyDoctor, 12, now, "I21.4");

        AddPatient("72030800834", "Piotr", "Krawczyk", new DateOnly(1972, 3, 8), PatientGender.Male,
            "Krwiak podtwardówkowy, zaburzenia świadomości", TriageCategory.Orange, emergency, emergencyDoctor, 8, now, "S06.5");

        AddPatient("55092100293", "Barbara", "Michalak", new DateOnly(1955, 9, 21), PatientGender.Female,
            "Ciężka niewydolność oddechowa", TriageCategory.Yellow, emergency, emergencyDoctor, 35, now, "J96.1");

        AddPatient("88020400820", "Michał", "Król", new DateOnly(1988, 2, 4), PatientGender.Male,
            "Złamanie otwarte kości przedramienia", TriageCategory.Yellow, emergency, emergencyDoctor, 50, now, "S52.5");

        AddPatient("63071700272", "Halina", "Pawlak", new DateOnly(1963, 7, 17), PatientGender.Female,
            "Zatorowość płucna", TriageCategory.Red, internalZone, internalDoctor, 25, now, "I26.9");

        AddPatient("78051900856", "Andrzej", "Górski", new DateOnly(1978, 5, 19), PatientGender.Male,
            "Ciężkie zatrucie pokarmowe z odwodnieniem", TriageCategory.Yellow, internalZone, internalDoctor, 70, now, "A05.9");

        AddPatient("34010600213", "Grażyna", "Nowak", new DateOnly(1934, 1, 6), PatientGender.Female,
            "Ostra niewydolność serca — obrzęki", TriageCategory.Green, internalZone, internalDoctor, 120, now, "I50.9");

        AddPatient("41082300881", "Tadeusz", "Lis", new DateOnly(1941, 8, 23), PatientGender.Male,
            "Hipoglikemia — stan podostry", TriageCategory.Green, internalZone, internalDoctor, 95, now, "E16.2");

        AddPatient("96031800838", "Jakub", "Baran", new DateOnly(1996, 3, 18), PatientGender.Male,
            "Upadek z wysokości — złamanie miednicy", TriageCategory.Orange, trauma, traumaDoctor, 18, now, "S32.4");

        AddPatient("69022600222", "Iwona", "Dąbrowska", new DateOnly(1969, 2, 26), PatientGender.Female,
            "Zwichnięcie barku", TriageCategory.Yellow, trauma, traumaDoctor, 45, now, "S43.4");

        AddPatient("11110500913", "Stanisław", "Jabłoński", new DateOnly(2011, 11, 5), PatientGender.Male,
            "Złamanie kości nadgarstka", TriageCategory.Green, trauma, traumaDoctor, 80, now, "S62.1");

        void AddPatient(
            string pesel,
            string firstName,
            string lastName,
            DateOnly birthDate,
            PatientGender gender,
            string complaint,
            TriageCategory category,
            Zone zone,
            User doctor,
            int minutesInZone,
            DateTimeOffset referenceTime,
            string? icd10)
        {
            var assignedAt = referenceTime.AddMinutes(-minutesInZone);
            var patient = Patient.Register(
                _idGenerator.NewId(),
                pesel,
                firstName,
                lastName,
                birthDate,
                gender,
                complaint,
                referenceTime.AddMinutes(-(minutesInZone + 15)));

            var assessment = TriageAssessment.Create(
                _idGenerator.NewId(),
                patient.Id,
                nurse.Id,
                category,
                $"Ocena wstępna: {complaint}",
                $"RR 140/90, HR 96, GCS {Random.Shared.Next(13, 16)}, SpO2 {Random.Shared.Next(92, 99)}%",
                referenceTime.AddMinutes(-minutesInZone));

            patient.AssignTriage(assessment);
            patient.AssignToZone(zone.Id, assignedAt);

            if (!string.IsNullOrWhiteSpace(icd10))
            {
                patient.SetDiagnosis(Icd10Code.Create(icd10));
            }

            _context.Patients.Add(patient);

            // Zlecenie towarzyszące — część pacjentów ma otwarte zlecenia (test reguły BR-10).
            if (category is TriageCategory.Red or TriageCategory.Orange)
            {
                patient.AddOrder(MedicalOrder.Create(
                    _idGenerator.NewId(),
                    patient.Id,
                    doctor.Id,
                    doctor.Role,
                    MedicalOrderType.Lab,
                    "Pakiet laboratoryjny pilny — morfologia, koagulogram, elektrolity, gazometria",
                    referenceTime.AddMinutes(-minutesInZone + 2),
                    isUrgent: true));
            }
        }
    }
}
