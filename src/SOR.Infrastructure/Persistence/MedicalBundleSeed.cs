using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Dane referencyjne: pakiety medyczne (zestawy postępowania) dla typowych stanów nagłych.
///
/// <para>
/// Pakiet to wzorzec, nie automatyczne zlecenie — po zastosowaniu każda pozycja staje się
/// zwykłym, edytowalnym zleceniem lekarskim, więc lekarz zachowuje pełną kontrolę (BR-13).
/// Zawartość odzwierciedla typowe postępowanie na izbie przyjęć i ma charakter wyłącznie
/// poglądowy; przed użyciem w praktyce wymaga weryfikacji z wytycznymi oddziału.
/// </para>
/// </summary>
public static class MedicalBundleSeed
{
    /// <summary>Buduje pakiety medyczne wraz z pozycjami.</summary>
    /// <param name="idGenerator">Generator identyfikatorów.</param>
    /// <param name="medications">Katalog leków — pozycje farmakologiczne odwołują się do jego kodów.</param>
    public static IReadOnlyList<MedicalBundle> Build(
        IIdGenerator idGenerator,
        IReadOnlyList<Medication> medications)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(medications);

        var bundles = new List<MedicalBundle>();

        bundles.Add(BuildOstryBólBrzucha(idGenerator, medications));
        bundles.Add(BuildUrazGlowy(idGenerator, medications));
        bundles.Add(BuildOstraDusznosc(idGenerator, medications));
        bundles.Add(BuildWazOsokrwiajacy(idGenerator, medications));
        bundles.Add(BuildZatrucieBenzodiazepinami(idGenerator, medications));
        bundles.Add(BuildZapaleniePluc(idGenerator, medications));
        bundles.Add(BuildHipoglikemia(idGenerator, medications));
        bundles.Add(BuildZespolCzeka(idGenerator, medications));

        return bundles;
    }

    private static MedicalBundle BuildOstryBólBrzucha(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-001",
            "Ostry ból brzucha — diagnostyka i leczenie",
            "Pacjent z bólem brzucha trwającym dłużej niż 6 godzin, bez objawów sugerujących natychmiastową operację.",
            "Bóle brzucha");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Lab, "Morfologia krwi, elektrolity, glukoza, amylaza, próby wątrobowe", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Procedure, "USG jamy brzusznej", isUrgent: true);
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Lab, "Badanie ogólne moczu");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Lek przeciwbólowy — dypiron 1 g i.v.", MedicationRoute.Intravenous, FindId(medications, "LEK-EMG-0007"), "1 g i.v.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Medication,
            "Lek przeciwymiotny — metoklopramid", MedicationRoute.Intravenous, null, "wg zlecenia");
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Obserwacja kliniczna i kontrola parametrów życiowych co 2 godziny");

        return bundle;
    }

    private static MedicalBundle BuildUrazGlowy(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-002",
            "Uraz głowy — diagnostyka i postępowanie",
            "Pacjent po urazie głowy z utratą przytomności, bólem głowy lub wymiotami.",
            "Urazy");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Procedure, "TK głowy", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Lab, "Morfologia, koagulogram, grupa krwi");
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Imaging, "RTG odcinka szyjnego kręgosłupa");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Lek przeciwbólowy — paracetamol 1 g i.v.", MedicationRoute.Intravenous, FindId(medications, "LEK-EMG-0006"), "1 g i.v.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Medication,
            "Lek przeciwymiotny — metoklopramid", MedicationRoute.Intravenous, null, "wg zlecenia");
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Obserwacja neurologiczna — ocena świadomości co godzinę");

        return bundle;
    }

    private static MedicalBundle BuildOstraDusznosc(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-003",
            "Ostra duszność — postępowanie doraźne",
            "Pacjent z dusznością spoczynkową, saturacją poniżej 92% lub męczącym wysiłkiem oddechowym.",
            "Zaburzenia oddechowe");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Imaging, "RTG klatki piersiowej", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Lab, "gazometria, morfologia, BNP");
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Medication,
            "Tlenoterapia — maska z rezerwuarem 15 l/min", MedicationRoute.Inhalation, null, "do uzyskania SpO2 ≥ 94%");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Lek rozszerzający oskrzela — tiotropium, nebulizacja", MedicationRoute.Inhalation,
            FindId(medications, "LEK-EMG-0093"), "2,5 mg nebulizacja");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Medication,
            "Glikokortykosteroid — hydrokortyzon 200 mg i.v.", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0071"), "200 mg i.v.");
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Kontrola saturacji i częstości oddechu co 15 minut");

        return bundle;
    }

    private static MedicalBundle BuildWazOsokrwiajacy(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-004",
            "Wąż osokrwiający — postępowanie",
            "Podejrzenie zakażenia układu krwionośnego, gorączka i objawy zakażenia uogólnionego.",
            "Zakażenia");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Lab, "Posiew krwi — 2 komplety, przed antybiotykoterapią", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Lab, "Morfologia, CRP, prokalcytonina, koagulogram", isUrgent: true);
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Medication,
            "Antybiotyk — ceftriakson 2 g i.v.", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0010"), "2 g i.v.", isUrgent: true);
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Płynoterapia — NaCl 0,9% 500 ml", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0030"), "500 ml i.v.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Consultation, "Konsultacja zakaźna", isUrgent: true);
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Kontrola parametrów życiowych co godzinę");

        return bundle;
    }

    private static MedicalBundle BuildZatrucieBenzodiazepinami(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-005",
            "Zatrucie benzodiazepinami — postępowanie",
            "Pacjent z depresją oddechową po przyjęciu benzodiazepin, zachowujący kontakt lub nie.",
            "Zatrucia");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Lab, "Gazometria, glukoza, próby wątrobowe, toksykogram", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Observation, "Monitorowanie oddechu — przygotowanie do intubacji");
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Medication,
            "Flumazenil 0,2 mg i.v.", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0101"), "0,2 mg i.v. stopniowo");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Flumazenil — dawka powtórna w razie ponownej depresji oddechowej", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0101"), "0,2 mg i.v.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Consultation, "Konsultacja toksykologiczna", isUrgent: true);
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Obserwacja przez co najmniej 6 godzin");

        return bundle;
    }

    private static MedicalBundle BuildZapaleniePluc(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-006",
            "Pneumonia — diagnostyka i leczenie",
            "Gorączka, duszność i zmiany osłuchowe sugerujące zapalenie płuc.",
            "Zakażenia");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Imaging, "RTG klatki piersiowej", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Lab, "Morfologia, CRP, prokalcytonina", isUrgent: true);
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Lab, "Posiew plwociny przed antybiotykoterapią");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Antybiotyk — ceftriakson 1 g i.v.", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0010"), "1 g i.v.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Medication,
            "Lek przeciwgorączkowy — paracetamol 1 g", MedicationRoute.Oral,
            FindId(medications, "LEK-EMG-0006"), "1 g p.o.");
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Observation, "Kontrola saturacji i częstości oddechu");

        return bundle;
    }

    private static MedicalBundle BuildHipoglikemia(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-007",
            "Hipoglikemia — postępowanie doraźne",
            "Glikemia poniżej 60 mg/dl z objawami neurologicznymi lub zaburzeniami świadomości.",
            "Zaburzenia metaboliczne");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Lab, "Glikemia — kontrola po 15 minutach", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Medication,
            "Glukoza 5% 500 ml", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0032"), "500 ml i.v.");
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Medication,
            "Glukagon 1 mg i.m. — przy pacjencie nieprzytomnym", MedicationRoute.Intramuscular,
            FindId(medications, "LEK-EMG-0080"), "1 mg i.m.");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Lab, "Elektrolity, kreatynina, osmolalność");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Observation, "Obserwacja do czasu ustąpienia objawów");

        return bundle;
    }

    private static MedicalBundle BuildZespolCzeka(IIdGenerator id, IReadOnlyList<Medication> medications)
    {
        var bundle = MedicalBundle.Create(
            id.NewId(),
            "PAK-SOR-008",
            "Zespół czeka — postępowanie w zespole wieńcowym",
            "Ból w klatce piersiowej podejrzany o niedokrwienie mięśnia sercowego.",
            "Kardiologia");

        bundle.AddItem(id.NewId(), 1, MedicalOrderType.Procedure, "EKG 12-kanałowe — powtórzyć po 15 minutach", isUrgent: true);
        bundle.AddItem(id.NewId(), 2, MedicalOrderType.Lab, "Troponina, CK-MB, morfologia, elektrolity, glukoza", isUrgent: true);
        bundle.AddItem(id.NewId(), 3, MedicalOrderType.Medication,
            "Kwas acetylosalicylowy 150 mg", MedicationRoute.Oral,
            FindId(medications, "LEK-EMG-0041"), "150 mg p.o.");
        bundle.AddItem(id.NewId(), 4, MedicalOrderType.Medication,
            "Heparyna drobnocząsteczkowa 5000 j.m.", MedicationRoute.Subcutaneous,
            FindId(medications, "LEK-EMG-0040"), "5000 j.m. s.c.");
        bundle.AddItem(id.NewId(), 5, MedicalOrderType.Medication,
            "Nitrogliceryna — wlew przy utrzymującym się bólu", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0050"), "5–10 µg/min");
        bundle.AddItem(id.NewId(), 6, MedicalOrderType.Medication,
            "Lek przeciwbólowy — morfina 3 mg i.v. w razie silnego bólu", MedicationRoute.Intravenous,
            FindId(medications, "LEK-EMG-0001"), "2–5 mg i.v.");
        bundle.AddItem(id.NewId(), 7, MedicalOrderType.Consultation, "Kardiolog — pilna konsultacja", isUrgent: true);
        bundle.AddItem(id.NewId(), 8, MedicalOrderType.Observation, "Monitorowanie EKG i parametrów życiowych w sposób ciągły");

        return bundle;
    }

    private static Guid? FindId(IReadOnlyList<Medication> medications, string code) =>
        medications.FirstOrDefault(medication => medication.Code == code)?.Id;
}
