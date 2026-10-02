using SOR.Domain.Common;
using SOR.Domain.Entities;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Dane referencyjne: katalog kodów ICD-10 w zakresie praktyki SOR.
///
/// <para>
/// Zbiór obejmuje rozdziały najczęściej wykorzystywane w segregacji i kodowaniu rozpoznań
/// w izbie przyjęć: choroby układu krążenia, oddechowego, nerwowego, urazy, zatrucia,
/// choroby zakaźne oraz objawy wymagające diagnostyki różnicowej. Opisy podano w polskiej
/// nomenklaturze kodowania chorób i problemów medycznych. Katalog pełni funkcję
/// podręcznika kodownika i podpowiedzi — nie zastępuje rozpoznania klinicznego.
/// </para>
/// </summary>
public static class Icd10CatalogSeed
{
    private const string ChapterCirculatory = "IX. Choroby układu krążenia";
    private const string ChapterRespiratory = "X. Choroby układu oddechowego";
    private const string ChapterDigestive = "XI. Choroby układu pokarmowego";
    private const string ChapterInfectious = "I. Choroby zakaźne";
    private const string ChapterInjury = "XIX. Urazy, zatrucia i inne następstwa zewnętrznych przyczyn";
    private const string ChapterSymptoms = "XVIII. Objawy, cechy chorobowe i nieprawidłowe wyniki badań";
    private const string ChapterNeuro = "VI. Choroby układu nerwowego";
    private const string ChapterMusculoskeletal = "XIII. Choroby układu mięśniowo-szkieletowego";
    private const string ChapterUrogenital = "XIV. Choroby układu moczowo-płciowego";
    private const string ChapterEndocrine = "IV. Choroby endokrynologiczne";
    private const string ChapterMental = "V. Zaburzenia psychiczne i zachowania";
    private const string ChapterSkin = "XII. Choroby skóry i tkanki podskórnej";

    /// <summary>Buduje katalog kodów ICD-10 dla typowych rozpoznań SOR.</summary>
    public static IReadOnlyList<Icd10CatalogEntry> Build(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        var entries = new List<Icd10CatalogEntry>
        {
            // ---------- Ból brzucha ----------
            Entry(idGenerator, "R10.4", "Ból brzucha — inny", ChapterSymptoms, "Bóle brzucha"),
            Entry(idGenerator, "R10.0", "Ostry ból brzucha w nadbrzuszu", ChapterSymptoms, "Bóle brzucha"),
            Entry(idGenerator, "R10.1", "Ostry ból brzucha w podbrzuszu", ChapterSymptoms, "Bóle brzucha"),
            Entry(idGenerator, "R10.2", "Ból brzucha w okolicy pępkowej", ChapterSymptoms, "Bóle brzucha"),
            Entry(idGenerator, "R10.3", "Ból brzucha w okolicy prawego podbrzusza", ChapterSymptoms, "Bóle brzucha"),
            Entry(idGenerator, "K52.9", "Nieokreślone zapalenie żołądka i jelit", ChapterDigestive, "Zapalenia przewodu pokarmowego"),
            Entry(idGenerator, "K29.0", "Ostre zapalenie żołądka z krwawieniem", ChapterDigestive, "Zapalenia przewodu pokarmowego"),
            Entry(idGenerator, "K25.0", "Wrzód trawienia z krwawieniem", ChapterDigestive, "Choroba wrzodowa"),
            Entry(idGenerator, "K80", "Kamica żółciowa", ChapterDigestive, "Choroby dróg żółciowych"),
            Entry(idGenerator, "K35.8", "Ostre zapalenie wyrostka robaczkowego, inne", ChapterDigestive, "Ostre brzuchy"),
            Entry(idGenerator, "K40.9", "Przepuklina brzuszna pępowinowa bez niedrożności", ChapterDigestive, "Przepukliny"),
            Entry(idGenerator, "K59.0", "Zaparcie", ChapterDigestive, "Zaburzenia czynności jelit"),
            Entry(idGenerator, "K21.9", "Choroba refluksowa żołądkowo-przełykowa bez zapalenia", ChapterDigestive, "Choroba refluksowa"),

            // ---------- Układ krążenia ----------
            Entry(idGenerator, "I20.0", "Niestabilna dławica piersiowa", ChapterCirculatory, "Ostry zespół wieńcowy"),
            Entry(idGenerator, "I21.0", "Ostry zawał serca ściany przedniej", ChapterCirculatory, "Ostry zespół wieńcowy"),
            Entry(idGenerator, "I21.4", "Ostry zawał serca ściany tylnej", ChapterCirculatory, "Ostry zespół wieńcowy"),
            Entry(idGenerator, "I46.1", "Migotanie przedsionków, nawrotne", ChapterCirculatory, "Zaburzenia rytmu"),
            Entry(idGenerator, "I48.0", "Migotanie przedsionków", ChapterCirculatory, "Zaburzenia rytmu"),
            Entry(idGenerator, "I49.9", "Nieokreślone zaburzenia rytmu serca", ChapterCirculatory, "Zaburzenia rytmu"),
            Entry(idGenerator, "I44.0", "Blok prawej odnogi pęczka Hisa", ChapterCirculatory, "Zaburzenia przewodzenia"),
            Entry(idGenerator, "I41.0", "Ostre zapalenie mięśnia sercowego", ChapterCirculatory, "Kardiomiopatie"),
            Entry(idGenerator, "I50.9", "Niewydolność serca, nieokreślona", ChapterCirculatory, "Niewydolność krążenia"),
            Entry(idGenerator, "I10", "Pierwotne nadciśnienie tętnicze", ChapterCirculatory, "Nadciśnienie tętnicze"),
            Entry(idGenerator, "I63.9", "Udar niedokrwienny mózgu, nieokreślony", ChapterNeuro, "Ostre uszkodzenie mózgu"),
            Entry(idGenerator, "I61.9", "Krwotok śródmózgowy, nieokreślony", ChapterNeuro, "Ostre uszkodzenie mózgu"),
            Entry(idGenerator, "I71.0", "Tętniak aorty, nieokreślony", ChapterCirculatory, "Choroby aorty"),
            Entry(idGenerator, "I87.2", "Żylak przełyku", ChapterCirculatory, "Choroby żył"),

            // ---------- Układ oddechowy ----------
            Entry(idGenerator, "J18.9", "Zapalenie płuc, nieokreślone", ChapterRespiratory, "Zakażenia układu oddechowego"),
            Entry(idGenerator, "J44.1", "POChP z zaostrzeniem, nieokreślonym", ChapterRespiratory, "Przewlekłe choroby dróg oddechowych"),
            Entry(idGenerator, "J45.9", "Astma, nieokreślona", ChapterRespiratory, "Przewlekłe choroby dróg oddechowych"),
            Entry(idGenerator, "J96.0", "Ostra niewydolność oddechowa", ChapterRespiratory, "Niewydolność oddechowa"),
            Entry(idGenerator, "J81", "Obrzęk płuc", ChapterRespiratory, "Niewydolność oddechowa"),
            Entry(idGenerator, "J93", "Odpma opłucnowa", ChapterRespiratory, "Choroby opłucnej"),
            Entry(idGenerator, "J90", "Wysięk opłucnowy, nieokreślony", ChapterRespiratory, "Choroby opłucnej"),
            Entry(idGenerator, "R06.0", "Duszność", ChapterSymptoms, "Objawy oddechowe"),
            Entry(idGenerator, "J04", "Ostre zapalenie krtani", ChapterRespiratory, "Zakażenia górnych dróg oddechowych"),
            Entry(idGenerator, "J02.9", "Ostre zapalenie gardła, nieokreślone", ChapterRespiratory, "Zakażenia górnych dróg oddechowych"),

            // ---------- Układ nerwowy ----------
            Entry(idGenerator, "G40.9", "Padaczka, nieokreślona", ChapterNeuro, "Napady padaczkowe"),
            Entry(idGenerator, "G41.9", "Stan padaczkowy", ChapterNeuro, "Napady padaczkowe"),
            Entry(idGenerator, "G20", "Choroba Parkinsona", ChapterNeuro, "Zaburzenia ruchowe"),
            Entry(idGenerator, "G43.9", "Migrena, nieokreślona", ChapterNeuro, "Bóle głowy"),
            Entry(idGenerator, "G45.9", "Przemijający niedokrwienny niedokrw mózgu, nieokreślony", ChapterNeuro, "Ostre uszkodzenie mózgu"),
            Entry(idGenerator, "G51.0", "Zespół Rossolimo-Melicharda", ChapterNeuro, "Zaburzenia nerwów czaszkowych"),
            Entry(idGenerator, "R47.0", "AFA — afazja", ChapterSymptoms, "Objawy neurologiczne"),
            Entry(idGenerator, "R55", "Omdlenie i kolaps", ChapterSymptoms, "Objawy neurologiczne"),
            Entry(idGenerator, "R56.9", "Drgawki, nieokreślone", ChapterSymptoms, "Napady drgawkowe"),

            // ---------- Urazy ----------
            Entry(idGenerator, "S06.0", "Wstrząśnienie mózgu", ChapterInjury, "Urazy głowy"),
            Entry(idGenerator, "S06.5", "Epiduralny krwiak podtwardówkowy", ChapterInjury, "Urazy głowy"),
            Entry(idGenerator, "S06.6", "Podtwardówkowe krwawienie pourazowe", ChapterInjury, "Urazy głowy"),
            Entry(idGenerator, "S01.0", "Rana skalpowata okolicy czubka głowy", ChapterInjury, "Urazy głowy"),
            Entry(idGenerator, "S72.0", "Złamanie szyjki kości udowej", ChapterInjury, "Urazy kończyn"),
            Entry(idGenerator, "S52.5", "Złamanie kości promieniowej", ChapterInjury, "Urazy kończyn"),
            Entry(idGenerator, "S42.2", "Złamanie trzonu kości ramiennej", ChapterInjury, "Urazy kończyn"),
            Entry(idGenerator, "S36.0", "Uraz brzucha: uszkodzenie żołądka", ChapterInjury, "Urazy brzucha"),
            Entry(idGenerator, "S39.0", "Uraz kręgosłupa lędźwiowego", ChapterInjury, "Urazy kręgosłupa"),
            Entry(idGenerator, "T14.9", "Uraz, nieokreślony", ChapterInjury, "Urazy nieokreślone"),
            Entry(idGenerator, "T78.0", "Reakcja anafilaktyczna na nieznany czynnik", ChapterInjury, "Reakcje alergiczne"),
            Entry(idGenerator, "T78.1", "Reakcja anafilaktyczna na pokarm", ChapterInjury, "Reakcje alergiczne"),
            Entry(idGenerator, "T50.9", "Zatrucie innymi i nieokreślonymi lekami", ChapterInjury, "Zatrucia"),
            Entry(idGenerator, "T39.1", "Zatrucie benzodiazepinami", ChapterInjury, "Zatrucia"),
            Entry(idGenerator, "T40.1", "Zatrucie opioidami", ChapterInjury, "Zatrucia"),
            Entry(idGenerator, "T58", "Zatrucie lekami działającymi na układ metaboliczny", ChapterInjury, "Zatrucia"),

            // ---------- Zakażenia ----------
            Entry(idGenerator, "A09", "Wirusowe zakażenie jelit, nieokreślone", ChapterInfectious, "Zakażenia przewodu pokarmowego"),
            Entry(idGenerator, "A04.7", "Zapalenie jelit z określonymi patogenami", ChapterInfectious, "Zakażenia przewodu pokarmowego"),
            Entry(idGenerator, "J15.9", "Bakteryjne zapalenie płuc, nieokreślone", ChapterInfectious, "Zakażenia układu oddechowego"),
            Entry(idGenerator, "L03.9", "Rozlewne zapalenie skóry, nieokreślone", ChapterSkin, "Zakażenia skóry i tkanek"),
            Entry(idGenerator, "B05.9", "Ospa wietrzna, nieokreślona", ChapterInfectious, "Zakażenia wirusowe"),
            Entry(idGenerator, "A15.0", "Gruźlica płuc", ChapterInfectious, "Zakażenia przewlekłe"),
            Entry(idGenerator, "B34.9", "Zakażenie wirusowe, nieokreślone", ChapterInfectious, "Zakażenia wirusowe"),

            // ---------- Układ mięśniowo-szkieletowy ----------
            Entry(idGenerator, "M54.5", "Ból krzyża (lumbago)", ChapterMusculoskeletal, "Bóle kręgosłupa"),
            Entry(idGenerator, "M79.6", "Bóle kończyn", ChapterMusculoskeletal, "Bóle mięśniowo-stawowe"),
            Entry(idGenerator, "M13.9", "Zapalenie stawu, nieokreślone", ChapterMusculoskeletal, "Choroby stawów"),
            Entry(idGenerator, "M25.5", "Ból stawu", ChapterMusculoskeletal, "Bóle mięśniowo-stawowe"),
            Entry(idGenerator, "M62.8", "Inne określone zaburzenia mięśni", ChapterMusculoskeletal, "Choroby mięśni"),

            // ---------- Układ moczowo-płciowy ----------
            Entry(idGenerator, "N20.0", "Kamica nerkowa", ChapterUrogenital, "Kamica moczowa"),
            Entry(idGenerator, "N17.9", "Ostre uszkodzenie nerek, nieokreślone", ChapterUrogenital, "Ostre uszkodzenie nerek"),
            Entry(idGenerator, "N39.0", "Zakażenie układu moczowego bez określenia miejsca", ChapterUrogenital, "Zakażenia układu moczowego"),
            Entry(idGenerator, "R31", "Krwawienie z dróg moczowych, nieokreślone", ChapterSymptoms, "Objawy układu moczowego"),
            Entry(idGenerator, "N83.2", "Torbiel jajnika", ChapterUrogenital, "Choroby narządu rodnego"),

            // ---------- Endokrynologia i zaburzenia metaboliczne ----------
            Entry(idGenerator, "E11.9", "Cukrzyca typu 2 bez powikłań", ChapterEndocrine, "Cukrzyca"),
            Entry(idGenerator, "E10.9", "Cukrzyca typu 1 bez powikłań", ChapterEndocrine, "Cukrzyca"),
            Entry(idGenerator, "E87.6", "Hipokaliemia", ChapterEndocrine, "Zaburzenia elektrolitowe"),
            Entry(idGenerator, "E87.1", "Hiponatremia", ChapterEndocrine, "Zaburzenia elektrolitowe"),
            Entry(idGenerator, "E88.0", "Kwasica metaboliczna", ChapterEndocrine, "Zaburzenia kwasowo-zasadowe"),
            Entry(idGenerator, "E78.5", "Hiperlipidemia, nieokreślona", ChapterEndocrine, "Zaburzenia metaboliczne"),
            Entry(idGenerator, "E03.9", "Niedoczynność tarczycy, nieokreślona", ChapterEndocrine, "Choroby tarczycy"),
            Entry(idGenerator, "E05.0", "Wole guzkowe nadczynne", ChapterEndocrine, "Choroby tarczycy"),

            // ---------- Zaburzenia psychiczne ----------
            Entry(idGenerator, "F10.6", "Zespół abstynencyjny — alkohol", ChapterMental, "Zaburzenia związane z substancjami"),
            Entry(idGenerator, "F19.6", "Zespół abstynencyjny — substancje psychotropowe", ChapterMental, "Zaburzenia związane z substancjami"),
            Entry(idGenerator, "F32.1", "Epizod depresyjny umiarkowany", ChapterMental, "Zaburzenia nastroju"),
            Entry(idGenerator, "F41.0", "Zaburzenie lękowe z napadami lęku (zaburzenie lękowe)", ChapterMental, "Zaburzenia lękowe"),

            // ---------- Objawy wymagające diagnostyki ----------
            Entry(idGenerator, "R07.4", "Ból w klatce piersiowej", ChapterSymptoms, "Bóle w klatce piersiowej"),
            Entry(idGenerator, "R50.9", "Gorączka, nieokreślona", ChapterSymptoms, "Objawy ogólne"),
            Entry(idGenerator, "R51", "Ból głowy", ChapterSymptoms, "Bóle głowy"),
            Entry(idGenerator, "R59.0", "Powiększenie regionalnych węzłów chłonnych", ChapterSymptoms, "Objawy ogólne"),
            Entry(idGenerator, "R60.0", "Obrzęk", ChapterSymptoms, "Objawy skóry i tkanki podskórnej"),
            Entry(idGenerator, "R68.3", "Wymioty", ChapterSymptoms, "Objawy przewodu pokarmowego"),
            Entry(idGenerator, "R53", "Złe samopoczucie", ChapterSymptoms, "Objawy ogólne"),
            Entry(idGenerator, "R45.0", "Nerwowość", ChapterMental, "Objawy psychiczne"),
            Entry(idGenerator, "R13", "Ból w gardle", ChapterSymptoms, "Objawy górnych dróg oddechowych"),
            Entry(idGenerator, "R25.2", "Drętwienie kończyny", ChapterSymptoms, "Objawy neurologiczne")
        };

        return entries;
    }

    private static Icd10CatalogEntry Entry(
        IIdGenerator idGenerator,
        string code,
        string description,
        string chapter,
        string? category = null) =>
        Icd10CatalogEntry.Create(idGenerator.NewId(), code, description, chapter, category, isEmergencyRelevant: true);
}
