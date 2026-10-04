using SOR.Domain.Common;
using SOR.Domain.Entities;

namespace SOR.Infrastructure.Persistence;

/// <summary>
/// Dane referencyjne: katalog oddziałów szpitalnych, do których SOR przekazuje pacjentów.
///
/// <para>
/// Lista odwzorowuje strukturę Szpitala Uniwersyteckiego nr 1 im. dr. Antoniego Jurasza
/// w Bydgoszczy — kliniki i oddziały objęte zakresem świadczeń (strona szpitala:
/// jurasza.umk.pl/kliniki). Lista służy wyłącznie do oznaczenia oddziału przyjmującego
/// w formularzu przekazania pacjenta; nie jest wykazem uprawnień ani dostępności łóżek.
/// </para>
/// </summary>
public static class DepartmentCatalogSeed
{
    /// <summary>Buduje pełny katalog oddziałów.</summary>
    public static IReadOnlyList<Department> Build(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        var departments = new (string Code, string Name)[]
        {
            ("AIT", "Klinika Anestezjologii i Intensywnej Terapii"),
            ("AITD", "Oddział Kliniczny Anestezjologii i Intensywnej Terapii dla Dzieci"),
            ("AITK", "Oddział Kliniczny Anestezjologii i Intensywnej Terapii z Pododdziałem Kardioanestezjologii"),
            ("CHD", "Klinika Chirurgii Dziecięcej"),
            ("OCU", "Klinika Chorób Oczu"),
            ("NAC", "Klinika Chirurgii Naczyniowej i Angiologii"),
            ("WAT", "Klinika Chirurgii Ogólnej, Chirurgii Wątroby i Transplantacyjnej"),
            ("PLA", "Klinika Chirurgii Plastycznej, Rekonstrukcyjnej i Estetycznej"),
            ("DER", "Klinika Dermatologii, Chorób Przenoszonych Drogą Płciową i Immunodermatologii"),
            ("END", "Klinika Endokrynologii, Diabetologii i Chorób Wewnętrznych"),
            ("GER", "Klinika Geriatrii i Chorób Wewnętrznych"),
            ("KAR", "Klinika Kardiochirurgii"),
            ("KIN", "Klinika Kardiologii i Chorób Wewnętrznych"),
            ("SOR", "Klinika Medycyny Ratunkowej"),
            ("NEF", "Klinika Nefrologii, Nadciśnienia Tętniczego i Chorób Wewnętrznych"),
            ("NCH", "Klinika Neurochirurgii, Neurotraumatologii i Neurochirurgii Dziecięcej"),
            ("NEU", "Klinika Neurologii"),
            ("ORT", "Klinika Ortopedii i Traumatologii Narządu Ruchu"),
            ("OTO", "Klinika Otolaryngologii, Onkologii Laryngologicznej, Audiologii i Foniatrii"),
            ("PED", "Klinika Pediatrii, Alergologii i Gastroenterologii"),
            ("PHO", "Klinika Pediatrii, Hematologii, Onkologii, Immunologii i Transplantologii"),
            ("PSY", "Klinika Psychiatrii"),
            ("REH", "Klinika Rehabilitacji"),
            ("URO", "Klinika Urologii Ogólnej i Onkologicznej"),
            ("TRG", "Klinika Transplantologii i Chirurgii Ogólnej"),
            ("PAL", "Oddział Medycyny Paliatywnej"),
        };

        return departments
            .Select(department => Department.Create(idGenerator.NewId(), department.Code, department.Name))
            .ToList();
    }
}