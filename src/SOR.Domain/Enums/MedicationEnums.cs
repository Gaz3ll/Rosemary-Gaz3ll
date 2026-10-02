namespace SOR.Domain.Enums;

/// <summary>Droga podania leku — determinuje sposób realizacji zlecenia na SOR.</summary>
public enum MedicationRoute
{
    /// <summary>Doustnie (per os).</summary>
    Oral = 1,

    /// <summary>Dożylnie.</summary>
    Intravenous = 2,

    /// <summary>Domięśniowo.</summary>
    Intramuscular = 3,

    /// <summary>Podskórnie.</summary>
    Subcutaneous = 4,

    /// <summary>Wewnątrzopłucnowo (płucna).</summary>
    Inhalation = 5,

    /// <summary>Na błonę śluzową (np. donosowo, podjęzykowo).</summary>
    Mucosal = 6,

    /// <summary>Donaczowo (krew i preparaty krwiopochodne).</summary>
    IntravenousBloodProduct = 7,

    /// <summary>Przez z sondę / sondę żołądkową.</summary>
    Tube = 8
}

/// <summary>Grupa terapeutyczna leku — upraszcza nawigację w katalogu.</summary>
public enum MedicationCategory
{
    /// <summary>Leki przeciwbólowe i przeciwzapalne.</summary>
    Analgesic = 1,

    /// <summary>Antybiotyki i leki przeciwinfekcyjne.</summary>
    Antibiotic = 2,

    /// <summary>Leki ratujące życie i resuscytacyjne.</summary>
    Resuscitation = 3,

    /// <summary>Leki przeciwzakrzepowe i antyagregacyjne.</summary>
    Antithrombotic = 4,

    /// <summary>Leki układu krążenia (kardiologiczne).</summary>
    Cardiovascular = 5,

    /// <summary>Leki neurologiczne i psychiatryczne.</summary>
    Neurologic = 6,

    /// <summary>Leki przeciwpadaczkowe.</summary>
    Antiepileptic = 7,

    /// <summary>Leki przeciwhistaminowe.</summary>
    Antihistamine = 8,

    /// <summary>Leki hormonalne i endokrynologiczne.</summary>
    Hormonal = 9,

    /// <summary>Leki metaboliczne (cukrzyca, gospodarka potasowa).</summary>
    Metabolic = 10,

    /// <summary>Płyny i preparaty krwiopochodne.</summary>
    FluidAndBlood = 11,

    /// <summary>Środki znieczulające i sedatywne.</summary>
    Anesthetic = 12,

    /// <summary>Leki miejscowe.</summary>
    LocalAnesthetic = 13,

    /// <summary>Leki stosowane w zatruciach i odtruwaniu.</summary>
    Toxicology = 14,

    /// <summary>Preparaty stosowane miejscowo (dezynfekcja, opatrunki).</summary>
    Topical = 15
}

/// <summary>Charakterystyka bezpieczeństwa leku — steruje ostrzeżeniami w interfejsie.</summary>
public enum MedicationSafety
{
    /// <summary>Lek standardowy, brak szczególnych ograniczeń.</summary>
    Standard = 1,

    /// <summary>Lek o przedłużonym uwalnianiu — nie należy kruszyć ani dzielić tabletki.</summary>
    ModifiedRelease = 2,

    /// <summary>Lek podwyższonego ryzyka (wymaga weryfikacji dawki i drogi podania).</summary>
    HighAlert = 3,

    /// <summary>Produkt krwiopochodny — wymaga zgodności dawki i grupy.</summary>
    BloodProduct = 4,

    /// <summary>Lek o działaniu antagonistycznym — grozi interakcją i maskowaniem objawów.</summary>
    Antidote = 5
}
