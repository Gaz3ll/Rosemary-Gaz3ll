using SOR.Domain.Common;
using SOR.Domain.Entities;
using SOR.Domain.Enums;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Dane referencyjne: katalog leków możliwych do podania w SOR.
///
/// <para>
/// Zawartość odwzorowuje typowy formularz oddziału ratunkowego — leki przeciwbólowe,
/// antybiotyki o szerokim spektrum, leki ratujące życie, antykoagulanty, preparaty dożylne
/// i odtrutniki. Opisy dawek mają charakter wyłącznie poglądowy i służą do zasilenia
/// interfejsu w ramach projektu; nie stanowią instrukcji klinicznej ani Charakterystyki
/// Produktu Lekarskiego. Wartości dawek należy zweryfikować zgodnie z aktualną ChPL
/// i wewnętrznymi standardami oddziału.
/// </para>
/// </summary>
public static class MedicationCatalogSeed
{
    /// <summary>Buduje pełny katalog leków.</summary>
    public static IReadOnlyList<Medication> Build(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        var medications = new List<Medication>
        {
            // ---------- Przeciwbólowe i przeciwzapalne ----------
            Create(idGenerator, "LEK-EMG-0001", "Morfina", "ampułka", "10 mg/ml",
                MedicationCategory.Analgesic, MedicationRoute.Intravenous,
                "2–5 mg i.v. w razie silnego bólu", "60 mg/dobę", MedicationSafety.HighAlert,
                "Depresja oddechowa — zawsze z dostępem do tlenu i worka Ambu.",
                "Podawanie wstrzyknięte, nie w bolusie — ze względu na ryzyko depresji oddechowej."),

            Create(idGenerator, "LEK-EMG-0002", "Fentanyl", "ampułka", "0,5 mg/2 ml",
                MedicationCategory.Analgesic, MedicationRoute.Intravenous,
                "0,05–0,1 mg i.v. przy silnym bólu", "0,5 mg/dobę", MedicationSafety.HighAlert,
                "Ryzyko depresji oddechowej i sztywności klatki piersiowej.",
                "Lek wyboru w bólu pourazowym; dawkować w bolusach po 25–50 µg."),

            Create(idGenerator, "LEK-EMG-0003", "Tramadol", "ampułka", "100 mg/2 ml",
                MedicationCategory.Analgesic, MedicationRoute.Intravenous,
                "50–100 mg i.v.", "400 mg/dobę", MedicationSafety.Standard,
                "Ryzyko napadów drgawkowych, interakcje z SSRI.",
                "Zastępnik morfiny przy bólu o nasileniu umiarkowanym."),

            Create(idGenerator, "LEK-EMG-0004", "Ketoprofen", "tabletki", "100 mg",
                MedicationCategory.Analgesic, MedicationRoute.Oral,
                "100 mg p.o.", "300 mg/dobę", MedicationSafety.Standard,
                "Wrzód trawienia, niewydolność nerek, nadwrażliwość na NLPZ.",
                "Nie stosować w podejrzeniu krwawienia z przewodu pokarmowego."),

            Create(idGenerator, "LEK-EMG-0005", "Ibuprofen", "tabletki", "400 mg",
                MedicationCategory.Analgesic, MedicationRoute.Oral,
                "400 mg p.o.", "1200 mg/dobę", MedicationSafety.Standard,
                "Wrzód trawienia, ciężka niewydolność nerek, astma aspirynowa.",
                null),

            Create(idGenerator, "LEK-EMG-0006", "Paracetamol", "tabletki", "500 mg",
                MedicationCategory.Analgesic, MedicationRoute.Oral,
                "500–1000 mg p.o.", "4000 mg/dobę", MedicationSafety.Standard,
                "Uszkodzenie wątroby — nie przekraczać 4 g/dobę, ostrożnie w alkoholuizmie.",
                "Lek pierwszego wyboru w bólu o małym i średnim nasileniu."),

            Create(idGenerator, "LEK-EMG-0007", "Dypiron (metamizol)", "ampułka", "1 g/2 ml",
                MedicationCategory.Analgesic, MedicationRoute.Intravenous,
                "500–1000 mg i.v.", "4000 mg/dobę", MedicationSafety.Standard,
                "Ryzyko agranulocytozy; ostrożnie w chorobach układu krwiotwórczego.",
                "Szybka droga podania, przydatna w bólu trzewnym."),

            // ---------- Antybiotyki ----------
            Create(idGenerator, "LEK-EMG-0010", "Ceftriakson", "ampułka", "1 g",
                MedicationCategory.Antibiotic, MedicationRoute.Intravenous,
                "1–2 g i.v. raz na dobę", "4 g/dobę", MedicationSafety.Standard,
                "Nadwrażliwość na cefalosporyny, cholestaza przy dużych dawkach.",
                "Podanie w ciągu infuzji; w odrażnieniu zabarwia linę naczyniową."),

            Create(idGenerator, "LEK-EMG-0011", "Metronidazol", "tabletki", "500 mg",
                MedicationCategory.Antibiotic, MedicationRoute.Oral,
                "500 mg p.o. 3 × dziennie", "1500 mg/dobę", MedicationSafety.Standard,
                "Disulfiram, alkohol — bezwzględny zakaz spożywania alkoholu w trakcie terapii.",
                "Lek pierwszego wyboru w zakażeniach jamy brzusznej."),

            Create(idGenerator, "LEK-EMG-0012", "Amoksycylina z kwasem klawulanowym", "tabletki", "875 mg/125 mg",
                MedicationCategory.Antibiotic, MedicationRoute.Oral,
                "875 mg p.o. 2 × dziennie", "1750 mg/dobę", MedicationSafety.Standard,
                "Nadwrażliwość na penicyliny; zwiększa ryzyko zapalenia jelita grubego.",
                null),

            Create(idGenerator, "LEK-EMG-0013", "Kwas nalidyksowy", "tabletki", "500 mg",
                MedicationCategory.Antibiotic, MedicationRoute.Oral,
                "500 mg p.o. 4 × dziennie", "2000 mg/dobę", MedicationSafety.Standard,
                "Nadwrażliwość, ryzyko zapalenia stawów, fotowrażliwość.",
                "Stosowany w zakażeniach układu moczowego."),

            Create(idGenerator, "LEK-EMG-0014", "Ciproksylacyna", "tabletki", "500 mg",
                MedicationCategory.Antibiotic, MedicationRoute.Oral,
                "500 mg p.o. 2 × dziennie", "1000 mg/dobę", MedicationSafety.Standard,
                "Uszkodzenie ścięgien, zaburzenia rytmu, interakcje z kofeiną i teofiliną.",
                "Unikać równoczesnego podawania z mlekiem i lekami obniżającymi potas."),

            // ---------- Resuscytacja i stan nagły ----------
            Create(idGenerator, "LEK-EMG-0020", "Adrenalina", "ampułka", "1 mg/ml",
                MedicationCategory.Resuscitation, MedicationRoute.Intravenous,
                "1 mg i.v. w zatrzymaniu krążenia; 0,3–0,5 mg i.m. w anafilaksji",
                "Brak sztywnego limitu w resuscytacji", MedicationSafety.HighAlert,
                "Fryktura kardiogenna w przypadku nieprawidłowej techniki podania.",
                "W zatrzymaniu krążenia podaje się nierozcieńczoną, w bolusie."),

            Create(idGenerator, "LEK-EMG-0021", "Atropina", "ampułka", "1 mg/ml",
                MedicationCategory.Resuscitation, MedicationRoute.Intravenous,
                "0,5–1 mg i.v. w bradykardii objawowej", "3 mg/dobę", MedicationSafety.HighAlert,
                "Jaskra z wąskim kątem, zwężenie zastawki mitralnej.",
                "Maksymalna łączna dawka 3 mg; powtarzać co 3–5 min."),

            Create(idGenerator, "LEK-EMG-0022", "Amiodaron", "ampułka", "150 mg/3 ml",
                MedicationCategory.Resuscitation, MedicationRoute.Intravenous,
                "150 mg i.v. przy migotaniu przedsionków z niestabilnością", "300 mg/dobę",
                MedicationSafety.HighAlert,
                "Długoterminowa toksyczność tarczycowa, uszkodzenie wątroby i płuc.",
                "Wymaga monitorowania EKG i elektrolitów."),

            Create(idGenerator, "LEK-EMG-0023", "Kwas salicylowy", "worek infuzyjny", "500 ml",
                MedicationCategory.FluidAndBlood, MedicationRoute.Intravenous,
                "250–500 ml i.v. w celu odwodnienia", "2–3 l/dobę wg wskazań",
                MedicationSafety.Standard,
                "Kwasica metaboliczna, przewodnienie; ostrożnie w niewydolności serca.",
                "Leczenie ostrego zatrucia kwasem salicylowym wymaga alkalizacji."),

            Create(idGenerator, "LEK-EMG-0024", "Kwas mlekowy (mleczan sodu)", "worek infuzyjny", "500 ml",
                MedicationCategory.FluidAndBlood, MedicationRoute.Intravenous,
                "500–1000 ml i.v. w kwasicy metabolicznej", "wg wskazań", MedicationSafety.Standard,
                "Kwasica mleczanowa, niewydolność serca.",
                "Bufor wodorowęglanowy w ciężkiej kwasicy metabolicznej."),

            // ---------- Płyny i preparaty krwiopochodne ----------
            Create(idGenerator, "LEK-EMG-0030", "NaCl 0,9%", "worek infuzyjny", "500 ml",
                MedicationCategory.FluidAndBlood, MedicationRoute.Intravenous,
                "500–1000 ml i.v.", "wg wskazań", MedicationSafety.Standard,
                "Przewodnienie, kwasica hiperchloremiczna.",
                "Podstawowy płyn krystaloidowy na SOR."),

            Create(idGenerator, "LEK-EMG-0031", "NaCl 0,9%", "worek infuzyjny", "1000 ml",
                MedicationCategory.FluidAndBlood, MedicationRoute.Intravenous,
                "1000 ml i.v.", "wg wskazań", MedicationSafety.Standard,
                "Przewodnienie, kwasica hiperchloremiczna.",
                null),

            Create(idGenerator, "LEK-EMG-0032", "Glukoza 5%", "worek infuzyjny", "500 ml",
                MedicationCategory.FluidAndBlood, MedicationRoute.Intravenous,
                "500 ml i.v.", "wg wskazań", MedicationSafety.Standard,
                "Przewodnienie, hiperglikemia.",
                "Podstawowy płyn podtrzymujący."),

            Create(idGenerator, "LEK-EMG-0033", "Krew pełna (koncentrat krwinek czerwonych)", "worek", "1 jednostka",
                MedicationCategory.FluidAndBlood, MedicationRoute.IntravenousBloodProduct,
                "1–2 jednostki zależnie od niedokrwistości", "wg zlecenia",
                MedicationSafety.BloodProduct,
                "Konflikt serologiczny, reakcja hemolityczna.",
                "Konieczna identyfikacja pacjenta i kontrola grupy krwi przed podaniem."),

            // ---------- Antykoagulanty ----------
            Create(idGenerator, "LEK-EMG-0040", "Heparyna drobnocząsteczkowa", "strzykawka", "5000 j.m./0,5 ml",
                MedicationCategory.Antithrombotic, MedicationRoute.Subcutaneous,
                "5000 j.m. s.c. 1–2 × dziennie", "wg wskazań", MedicationSafety.Standard,
                "Aktywne krwawienie, ciężka małopłytkowość, nieodwracalna niewydolność nerek.",
                "Nie rozpoczynać przy podejrzeniu krwawienia śródczaszkowego."),

            Create(idGenerator, "LEK-EMG-0041", "Kwas acetylosalicylowy", "tabletki", "75 mg",
                MedicationCategory.Antithrombotic, MedicationRoute.Oral,
                "75–150 mg p.o. raz na dobę", "300 mg/dobę", MedicationSafety.Standard,
                "Aktywne krwawienie, nadwrażliwość na salicylany.",
                "Podanie rozpuszczonej tabletki wymaga opłukania sondy."),

            Create(idGenerator, "LEK-EMG-0042", "Enoksaparyna", "strzykawka", "40 mg/0,4 ml",
                MedicationCategory.Antithrombotic, MedicationRoute.Subcutaneous,
                "40 mg s.c. raz na dobę", "80 mg/dobę", MedicationSafety.Standard,
                "Niewydolność nerek, masywna trombocytopenia, aktywne krwawienie.",
                null),

            // ---------- Kardiologiczne ----------
            Create(idGenerator, "LEK-EMG-0050", "Nitrogliceryna", "ampułka", "5 mg/ml",
                MedicationCategory.Cardiovascular, MedicationRoute.Intravenous,
                "5–10 µg/min w ostrym wieńcowym zespole wieńcowym", "20 µg/min",
                MedicationSafety.HighAlert,
                "Hipotonia, przeszkoda w drodze odpływu prawej komory, wrażliwość na nitraty.",
                "Rozpoczynać od najmniejszej dawki, kontrolować ciśnienie tętnicze."),

            Create(idGenerator, "LEK-EMG-0051", "Furosemid", "ampułka", "20 mg/2 ml",
                MedicationCategory.Cardiovascular, MedicationRoute.Intravenous,
                "20–40 mg i.v.", "160 mg/dobę", MedicationSafety.HighAlert,
                "Hipowolemia, hipokaliemia, nefropatia na tle przesolonych diuretyków.",
                "Kontrolować elektrolity po podaniu."),

            Create(idGenerator, "LEK-EMG-0052", "Kaptopril", "tabletki", "25 mg",
                MedicationCategory.Cardiovascular, MedicationRoute.Oral,
                "12,5–25 mg p.o.", "50 mg/dobę", MedicationSafety.Standard,
                "Hipotonia, obustronna tętnicza zwężenie tętnic nerkowych, hiperkaliemia.",
                "Podanie podjęzykowe działa szybciej przy podejrzeniu ostrego zespołu wieńcowego."),

            // ---------- Neurologiczne i przeciwpadaczkowe ----------
            Create(idGenerator, "LEK-EMG-0060", "Diazepam", "ampułka", "10 mg/2 ml",
                MedicationCategory.Neurologic, MedicationRoute.Intravenous,
                "5–10 mg i.v. w razie napadu drgawkowego", "40 mg/dobę",
                MedicationSafety.HighAlert,
                "Depresja oddechowa, miopatia, uzależnienie.",
                "Podawanie powolne; zawsze z monitorowaniem oddechu."),

            Create(idGenerator, "LEK-EMG-0061", "Lewetiracetam", "tabletki", "500 mg",
                MedicationCategory.Antiepileptic, MedicationRoute.Oral,
                "500 mg p.o. 2 × dziennie", "3000 mg/dobę", MedicationSafety.Standard,
                "Senność, zaburzenia zachowania; ostrożnie w niewydolności nerek.",
                "Brak istotnych interakcji z lekami przeciwpadaczkowymi."),

            Create(idGenerator, "LEK-EMG-0062", "Piracetam", "ampułka", "1 g/5 ml",
                MedicationCategory.Neurologic, MedicationRoute.Intravenous,
                "1–2 g i.v.", "10 g/dobę", MedicationSafety.Standard,
                "Niewydolność nerek, podrażnienie żył.",
                "Stosowany w leczeniu międzyobrzękowym."),

            Create(idGenerator, "LEK-EMG-0063", "Piracetam", "tabletki", "800 mg",
                MedicationCategory.Neurologic, MedicationRoute.Oral,
                "800 mg p.o. 3 × dziennie", "2400 mg/dobę", MedicationSafety.Standard,
                "Niewydolność nerek.",
                "Kontynuacja leczenia po wypisie."),

            // ---------- Przeciwhistaminowe i steroidy ----------
            Create(idGenerator, "LEK-EMG-0070", "Klemastyna", "ampułka", "2 mg/2 ml",
                MedicationCategory.Antihistamine, MedicationRoute.Intravenous,
                "2 mg i.v. w anafilaksji", "4 mg/dobę", MedicationSafety.Standard,
                "Sedacja; ostrożnie w jaskrze z wąskim kątem.",
                "Lek pierwszego rzutu w anafilaksji, obok adrenaliny."),

            Create(idGenerator, "LEK-EMG-0071", "Hydrokortyzon", "ampułka", "100 mg",
                MedicationCategory.Hormonal, MedicationRoute.Intravenous,
                "100–200 mg i.v.", "400 mg/dobę", MedicationSafety.Standard,
                "Hiperglikemia, wrzody trawienia, immunosupresja.",
                "Stosowany we wstrząsie anafilaktycznym i w astmie ciężkiej."),

            Create(idGenerator, "LEK-EMG-0072", "Dexametazon", "ampułka", "4 mg/ml",
                MedicationCategory.Hormonal, MedicationRoute.Intravenous,
                "4–8 mg i.v.", "16 mg/dobę", MedicationSafety.Standard,
                "Hiperglikemia, zaburzenia elektrolitowe.",
                "Pierwszy wybór w obrzęku krtani — nie hamuje reakcji anafilaktycznej."),

            // ---------- Metaboliczne ----------
            Create(idGenerator, "LEK-EMG-0080", "Glukagon", "ampułka", "1 mg",
                MedicationCategory.Metabolic, MedicationRoute.Intramuscular,
                "1 mg i.m. w hipoglikemii nieprzytomnego pacjenta", "1–2 mg",
                MedicationSafety.HighAlert,
                "Skojarzenie z inhibitorami DPP-4 osłabia odpowiedź glukagonową.",
                "Po podaniu konieczne natychmiastowe podanie glukozy i zasięgnięcie pomocy."),

            Create(idGenerator, "LEK-EMG-0081", "Insulina krótkodziałająca", "strzykawka", "100 j.m./ml",
                MedicationCategory.Metabolic, MedicationRoute.Subcutaneous,
                "5–10 j.m. s.c. w kwasicy ketonowej (schemat dawek korekcyjnych)",
                "wg schematu", MedicationSafety.HighAlert,
                "Hipoglikemia, hiperkaliemia, obrzęk płuc w dużych dawkach.",
                "Każde podanie wymaga kontroli glikemii i zapisu godziny."),

            Create(idGenerator, "LEK-EMG-0082", "Kwas foliowy", "tabletki", "5 mg",
                MedicationCategory.Metabolic, MedicationRoute.Oral,
                "5 mg p.o. raz na dobę", "5 mg/dobę", MedicationSafety.Standard,
                "Nadwrażliwość na kwas foliowy.",
                "Wspomaganie w niedokrwistości."),

            // ---------- Znieczulenie i znieczulenie miejscowe ----------
            Create(idGenerator, "LEK-EMG-0090", "Lidokaina", "ampułka", "1% (10 mg/ml)",
                MedicationCategory.LocalAnesthetic, MedicationRoute.Mucosal,
                "2–4 mg/kg miejscowo, maks. 200 mg", "300 mg/dobę",
                MedicationSafety.HighAlert,
                "Drgawki przy przekroczeniu dawki, methemoglobinemia.",
                "Zmniejszyć dawkę u chorych z niewydolnością wątrobową."),

            Create(idGenerator, "LEK-EMG-0091", "Lidokaina", "żel", "2%",
                MedicationCategory.LocalAnesthetic, MedicationRoute.Mucosal,
                "warstwa miejscowo przed zabiegiem", "wg zabiegu",
                MedicationSafety.Standard,
                "Nie stosować na błony śluzowe uszkodzone.",
                "Znieczulenie miejscowe przy opracowywaniu ran."),

            Create(idGenerator, "LEK-EMG-0092", "Midazolam", "ampułka", "5 mg/ml",
                MedicationCategory.Anesthetic, MedicationRoute.Intravenous,
                "1–2 mg i.v. sedacja krótkotrwała", "10 mg/dobę",
                MedicationSafety.HighAlert,
                "Depresja oddechowa, benzodiazepinowa choroba odstawienna.",
                "Konieczne monitorowanie oddechu i obecność odsysacza."),

            Create(idGenerator, "LEK-EMG-0093", "Tiotropium", "roztwór do inhalacji", "2,5 ml",
                MedicationCategory.Resuscitation, MedicationRoute.Inhalation,
                "2,5 mg nebulizacja", "5 mg/dobę", MedicationSafety.Standard,
                "Jaskra, zatrzymanie moczu.",
                "Leki rozszerzające oskrzela w zaostrzeniu astmy i POChP."),

            // ---------- Odtrutniki i zatrucia ----------
            Create(idGenerator, "LEK-EMG-0100", "Nalokson", "ampułka", "0,4 mg/ml",
                MedicationCategory.Toxicology, MedicationRoute.Intravenous,
                "0,4–2 mg i.v. w depresji oddechowej", "wg wskazań",
                MedicationSafety.Antidote,
                "Krótki okres półtrwania — konieczne powtarzanie dawki.",
                "Odwraca depresję oddechową po opioidach."),

            Create(idGenerator, "LEK-EMG-0101", "Flumazenil", "ampułka", "0,5 mg/5 ml",
                MedicationCategory.Toxicology, MedicationRoute.Intravenous,
                "0,2 mg i.v. stopniowo", "1 mg", MedicationSafety.Antidote,
                "Ryzyko napadów drgawkowych u osób leczonych benzodiazepinami przewlekle.",
                "Antagonista benzodiazepin."),

            Create(idGenerator, "LEK-EMG-0102", "Atropina siarczan", "ampułka", "1 mg/ml",
                MedicationCategory.Toxicology, MedicationRoute.Intravenous,
                "1–2 mg i.v., dawkować wg objawów", "wg wskazań",
                MedicationSafety.Antidote,
                "W zatruciu inhibitorami cholinesterazy wymagane są duże dawki.",
                "Antagonista objawów cholinergicznych."),

            // ---------- Miejscowe i znieczulenie powierzchowne ----------
            Create(idGenerator, "LEK-EMG-0110", "Chlorheksydyna", "płyn", "0,5%",
                MedicationCategory.Topical, MedicationRoute.Mucosal,
                "miejscowo na ranę", "wg zabiegu", MedicationSafety.Standard,
                "Nie stosować na błony śluzowe i do oczu; rzadka anafilaksja.",
                "Dezynfekcja ran przed opracowaniem chirurgicznym."),

            Create(idGenerator, "LEK-EMG-0111", "Opatrunki hemostatyczne", "opakowanie", "1 szt.",
                MedicationCategory.Topical, MedicationRoute.Mucosal,
                "miejscowo w miejsce krwawienia", "wg zabiegu", MedicationSafety.Standard,
                null,
                "Tamponada ran przed założeniem szwów.")
        };

        return medications;
    }

    private static Medication Create(
        IIdGenerator idGenerator,
        string code,
        string name,
        string form,
        string strength,
        MedicationCategory category,
        MedicationRoute route,
        string typicalDose,
        string maxDailyDose,
        MedicationSafety safety = MedicationSafety.Standard,
        string? contraindications = null,
        string? notes = null) =>
        Medication.Create(
            idGenerator.NewId(),
            code,
            name,
            form,
            strength,
            category,
            route,
            typicalDose,
            maxDailyDose,
            safety,
            isAvailable: true,
            contraindications,
            notes);
}
