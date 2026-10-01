namespace SOR.Domain.Enums;

/// <summary>Fizyczna strefa oddziału SOR (moduł wstępny Triage traktowany jest jako strefa organizacyjna).</summary>
public enum ZoneKind
{
    /// <summary>Moduł wstępny: punkt przyjęć, rejestracja i segregacja medyczna pacjentów.</summary>
    Triage = 0,

    /// <summary>Część ratunkowa — obszar resuscytacyjno-zabiegowy (Trauma Room), Kod Czerwony.</summary>
    Emergency = 1,

    /// <summary>Część internistyczna — obserwacja, diagnostyka wielonarządowa, bilans płynów.</summary>
    Internal = 2,

    /// <summary>Część urazowo-ortopedyczna — zaopatrzenie chirurgiczne, procedury urazowe.</summary>
    Trauma = 3
}