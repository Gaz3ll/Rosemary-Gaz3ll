# ETAP III — Implementacja, testy i uruchomienie

## 1. Struktura rozwiązania

```
SOR.MedicalEmergencySystem.slnx
├── src/
│   ├── SOR.Domain/          # encje, VO, reguły, usługi i zdarzenia domenowe
│   ├── SOR.Application/     # kontrakty, DTO, serwisy aplikacyjne, mapowanie
│   ├── SOR.Infrastructure/  # EF Core + SQLite, repozytoria, UoW, seeder, DI
│   └── SOR.Presentation/    # WPF (MVVM): widoki, ViewModele, komendy
├── tests/
│   └── SOR.Domain.Tests/    # testy jednostkowe i integracyjne (xUnit)
├── docs/                    # dokumentacja ETAP I–III
└── database/                # miejsce na plik bazy (opcjonalne)
```

## 2. Kompilacja i uruchomienie

Wymagania: **.NET SDK 10** (runtime WPF .NET 9 dostępny w systemie), Windows.

```powershell
# kompilacja całego rozwiązania
dotnet build SOR.MedicalEmergencySystem.slnx

# testy
dotnet test SOR.MedicalEmergencySystem.slnx

# uruchomienie aplikacji desktopowej
dotnet run --project src/SOR.Presentation/SOR.Presentation.csproj
```

Baza danych tworzona jest automatycznie przy pierwszym uruchomieniu w
`%LOCALAPPDATA%\SOR\sor.db`. Dane startowe (strefy, konta, pacjenci, dyżury) są wypełniane
przez `DatabaseSeeder` i operacja jest idempotentna. `DatabaseInitializer` wykrywa nieaktualny
schemat (brakującą tabelę **lub kolumnę**, przez `PRAGMA table_info`) i odtwarza go od nowa
z reseedem danych.

### Konta demonstracyjne

| Login | Hasło | Rola | Strefa z grafiku |
|---|---|---|---|
| `ordynator` | `SOR2026!ord` | Koordynator | EMG (koordynacja) |
| `lekarz.emg` | `SOR2026!emg` | Lekarz | EMG |
| `lekarz.trm` | `SOR2026!trm` | Lekarz | TRM |
| `lekarz.int` | `SOR2026!int` | Lekarz | INT |
| `ratownik.emg` | `SOR2026!remg` | Ratownik medyczny | EMG |
| `ratownik.trm` | `SOR2026!rtrm` | Ratownik medyczny | TRM |
| `ratownik.int` | `SOR2026!rint` | Ratownik medyczny | INT |
| `piel.triage` | `SOR2026!tri` | Pielęgniarka | TRI |
| `piel.trm` | `SOR2026!pt` | Pielęgniarka | TRI |

Ratownik medyczny dyżuruje wyłącznie w strefach klinicznych (EMG, TRM, INT) — moduł wstępny
TRI obsługują pielęgniarki. Konta ratowników są tworzone razem z danymi startowymi, a przy
kolejnych uruchomieniach `DatabaseSeeder.EnsureParamedicAccountsAsync` uzupełnia nimi bazę,
która powstała jeszcze przed dodaniem tej roli.

Uprawnienia ratownika medycznego (BR-13):

| Czynność | Ratownik medyczny |
|---|---|
| Oznaczenie zlecenia podania leku jako zrealizowanego | Tak |
| Oznaczenie zlecenia badania obrazowego jako zrealizowanego | Tak |
| Oznaczenie zlecenia laboratoryjnego, zabiegu, konsultacji lub obserwacji | Nie — zostaje w gestii lekarza |
| Wystawienie zlecenia lekarskiego | Nie — lekarz lub koordynator |
| Postawienie rozpoznania ICD-10, zastosowanie pakietu medycznego | Nie — lekarz lub koordynator |
| Zamknięcie karty pacjenta | Nie — lekarz lub koordynator |

### Strefy i pojemności

| Kod | Nazwa | Rodzaj | Pojemność |
|---|---|---|---|
| `TRI` | Moduł wstępny — Triage | Triage | 12 |
| `EMG` | Część ratunkowa (Trauma Room) | Emergency | 6 |
| `INT` | Część internistyczna | Internal | 14 |
| `TRM` | Część urazowo-ortopedyczna | Trauma | 8 |

## 3. Warstwa prezentacji (MVVM)

- **`App.xaml.cs`** — Composition Root: buduje kontener, inicjalizuje bazę, tworzy jeden zakres
  i otwiera `MainWindow`. Obsługuje też globalne wyjątki oraz wymusza kulturę `pl-PL`
  i format daty `dd.MM.yyyy` (patrz [rozdział 9](#9-motyw-kolorystyczny-i-tryb-ciemny)).
- **`MainViewModel`** — punkt kompozycji widoku (łączy `SessionViewModel`, `PatientBoardViewModel`,
  `MedicationCatalogViewModel` i `PatientStayListViewModel`), stan sekcji (`WorkspaceSection`),
  komendy paska ikon oraz etykiety `LOGIN NAZWISKO_IMIĘ (ROLA)` i `Strefa: …`.
- **`SessionViewModel`** — logowanie, kontekst strefy, wniosek o zmianę strefy, rekomendacje rotacji.
- **`PatientBoardViewModel`** — rejestracja, Triage, kolejki pacjentów, karta pacjenta (tożsamość,
  lista podanych leków, operacje blokady i zamknięcia karty), obciążenie strefy. Zlecanie procedur,
  badań i konsultacji, rozpoznanie ICD-10 oraz wypis przeniesiono do okna „Historia pobytu".
- **`PatientStayListViewModel`** — ekran po zalogowaniu: jednostka organizacyjna i wyszukiwarka,
  zakres dat z nawigacją krokową, filtry statusowe, wiersze tabeli, raport z dyżuru i badania
  obrazowe (patrz [rozdział 8](#8-moduł-przyjęć--karta-pobytu-raport-z-dyżuru-i-badania-obrazowe)).
- **`MedicationCatalogViewModel`** — formularz leków oddziału: wyszukanie preparatu oraz złożenie
  zlecenia lub odnotowanie podania na karcie pacjenta. Pakiety medyczne i katalog rozpoznań ICD-10
  przeniesiono do okna „Historia pobytu".
- **`PatientHistoryViewModel`** — okno „Historia pobytu": podgląd przebiegu pobytu (dane przyjęcia,
  oceny Triage, przeniesienia, zlecenia, leki, wypisy) oraz **jedyne** miejsce zlecania procedur,
  badań, konsultacji i leków, stosowania pakietów medycznych, wyboru rozpoznania ICD-10 z katalogu
  (z wyszukiwaniem po kodzie i nazwie) i wypisu pacjenta; otwierane przez
  `IPatientHistoryDialogService` z listy pobytów i pulpitu strefy (patrz [rozdział 8](#8-moduł-przyjęć--karta-pobytu-raport-z-dyżuru-i-badania-obrazowe)).
- **`MainWindow.xaml`** — ekran logowania oraz ekran roboczy opisany w [rozdziale 7](#7-ekran-po-zalogowaniu).
  Konwertery `BooleanToVisibilityConverter`, `InverseBooleanToVisibilityConverter`,
  `StringToVisibilityConverter`, `PolishEnumConverter` i `EnumEqualsConverter` utrzymują widok
  deklaratywnym.
- **`UiThreadDispatcher`** — marshaling operacji na wątek UI.
- **`ExceptionMessageMapper`** — tłumaczenie wyjątków na komunikaty dla użytkownika.

Bindingi do właściwości tylko do odczytu (`ProgressBar.Value`, kolumny `DataGrid`,
`TabControl.SelectedIndex`) są jawnie `Mode=OneWay`, aby uniknąć wyjątku
„a TwoWay binding cannot work on a read-only property”.

## 4. Testy

Łącznie **112 testów** (xUnit), wszystkie przechodzą. Kluczowe grupy:

### `PersistenceTests` — trwałość (6)

| Test | Co weryfikuje |
|---|---|
| `EnsureCreated_TworzyPoprawnySchemat_BezBledowModelu` | Poprawność mapowania EF i utworzenie schematu |
| `Seed_JestIdempotentny_NieDuplikujeDanych` | Idempotencja danych startowych |
| `DiagnozaIcd10_JestZapisywanaWRelacjiOwnedType` | Zapis rozpoznania ICD-10 w relacji typu właściciela |
| `UzasadnienieZmianyStrefy_JestZapisaneWRepozytorium` | Zapis uzasadnienia rotacji |
| `DziennikAudytu_ZapisujeNieudanaProbeLogowania` | Audyt nieudanej próby logowania |
| `SchematNieaktualny_PowodujeOdtworzenieBazyIKatalogow` | Odtworzenie bazy i katalogów po zmianie modelu |

### `PeselNumberTests` — walidacja numeru PESEL (41 przypadków, BR-18)

| Test | Reguła / cel |
|---|---|
| `PoprawnyPesel_OdczytujeDateIGene` (7 przypadków) | Cyfra kontrolna, zakodowana data i płeć |
| `PrawidlowyPeselZRejestru_JestAkceptowany` (2 przypadki) | Numery zgodne z rozporządzeniem, które odrzucała suma modulo 11 (`02070803628`, `44051401458`) |
| `NumerPoprawnyTylkoWedługSumyModulo11_JestOdrzucony` (2 przypadki) | Numer uznawany wcześniej za poprawny, a formalnie nieistniejący |
| `CyfraKontrolnaGdyOstatniaCyfraSumyJestZero_RównaSieZero` | Gdy ostatnia cyfra sumy wynosi 0, cyfrą kontrolną jest 0 |
| `NiepoprawnaSumaKontrolna_JestOdrzucona` | Zmiana cyfry kontrolnej odrzuca numer |
| `NumerNiepelnejDlugosci_LubZNieCyfr_JestOdrzucony` (6 przypadków) | 11 cyfr, bez liter i separatorów |
| `SeparatoryWNumerzeSaIgnorowane` | Spacje, myślniki i kropki są pomijane |
| `NieistniejacaData_Urodzin_JestOdrzucona` | 30 lutego i 31 kwietnia — suma kontrolna poprawna, data nie istnieje |
| `NiepoprawnyMiesiac_Zkodowania_JestOdrzucony` | Miesiąc 13 poza zakresami 01–12/21–32/41–52/61–72/81–92 |
| `NumerZeStuleciaPoprzedniego_JestObslugiwany` | Kobieta urodzona w 1934 r. (miesiąc +20) |
| `TryDecode_WyliczaDateUrDZINaIPlecZNumeru` (3 przypadki) | Odczyt daty i płci z surowego wejścia |
| `TryDecode_OdrzucaNumerNiepoprawny` (6 przypadków) | Długość, suma kontrolna, data |
| `MatchesGender_RozpoznajeZgodnaZNiezgodnaPlec` | Zgodność płci z formularzem; `Other` nie pasuje do żadnego numeru |
| `RejestracjaPacjenta_*` (8 przypadków) | Rejestracja odrzuca rozbieżność płci i daty, akceptuje spójne dane (w tym XX i XXI wiek) |

```powershell
dotnet test tests/SOR.Domain.Tests/SOR.Domain.Tests.csproj --filter "FullyQualifiedName~PeselNumberTests"
```

### `ScenarioTests` — scenariusze integracyjne (14 przypadków)

| Test | Reguła / cel |
|---|---|
| `KontenerDi_RozwiazujeKompletSerwisow_BezCykluZaleznosci` | Acykliczny graf DI |
| `LogowanieKontekstowe_PrzypisujeStrefeZGrafiku` (3 przypadki) | BR-02/BR-16 |
| `BledneHaslo_NieUjawniaSzczegolowKryptograficznych` | Bezpieczeństwo |
| `ManualnaZmianaStrefy_ZapisujeUzasadnienieIAudyt` | BR-05/BR-16 |
| `ZmianaCudzejStrefy_PrzezLekarza_JestOdrzucona` | BR-08 |
| `KoordynatorMozeZlecicRotacje_InnegoPracownika` | BR-08 + zdarzenie domenowe |
| `RotacjaDoPelnejStrefy_JestOdrzucona_IZapisujeAudyt` | BR-04 |
| `Monitoring_KlasyfikujeObciazenie_BezAlarmuPrzyStabilnymStanie` | BR-06 |
| `TriageWStrefieKlinicznej_PrzydzielaAutomatycznieDoTejStrefy` | WF-04 — przydział automatyczny |
| `TriageWModuleWstepnym_PozostajeBezStrefy_DoPrzydzialuRecznego` | WF-04 — moduł triage (TRI) |
| `TriageWPelnejStrefie_NieCofaOceny_PacjentCzekaNaPrzydzial` | BR-04 + zachowanie oceny Triage |
| `HistoriaPobytu_ZawieraWszystkieOcenyTriage` | Historia pobytu — kolejne oceny Triage nie nadpisują poprzednich, format czasu |

Uruchomienie samych scenariuszy:

```powershell
dotnet test tests/SOR.Domain.Tests/SOR.Domain.Tests.csproj --filter "FullyQualifiedName~ScenarioTests"
```

### `ParamedicPermissionsTests` — uprawnienia ratownika medycznego (17)

| Test | Reguła / cel |
|---|---|
| `SeedTworzyJednoKontoRatownikaWKazdejStrefieKlinicznej` | Komplet kont EMG/INT/TRM |
| `RatownikNieMaKontaWStrefieTriage` | Brak ratownika w module wstępnym |
| `RatownikLogujeSieDoWlasnejStrefy` (3 przypadki) | BR-02/BR-16 dla nowej roli |
| `RatownikRealizujeZlecenieLekuIBadaniaObrazowego` (2 przypadki) | BR-13 — realizacja zleceń |
| `RatownikNieRealizujeZlecenInnychTypow` (4 przypadki) | BR-13 — lab, zabieg, konsultacja, obserwacja |
| `RatownikNieWystawiaZlecenLekarskich` | BR-13 |
| `RatownikNieUstawiaRozpoznania` | BR-13 |
| `RatownikNieStosujePakietuMedycznego` | BR-13 |
| `RatownikNieWypisujePacjentaZSor` (3 przypadki) | BR-11/BR-13 - brak uprawnienia do wypisu |
| `LekarzNadalRealizujeKazdeZlecenie` | Brak regresji dla lekarza |
| `SeedUzupelniaKontaRatownikowWJuzyIstniejacejBazie` | Uzupełnianie bazy sprzed dodania roli |

### `DischargeTests` - wypis pacjenta z SOR (14)

| Test | Regula / cel |
|---|---|
| `ZakonczenieLeczenia_BezRozpoznania_JestZablokowane` | BR-09 - wymagane rozpoznanie ICD-10 |
| `ZakonczenieLeczenia_BlokujeOtwarteZlecenia` | BR-10 |
| `ZakonczenieLeczenia_PrzySpelnionychWarunkach_UstawiaStanZamkniety` | BR-11 - stan `Closed` i zapis historii |
| `WypisNaWlasneZadanie_BezPowodu_JestZablokowany` | BR-11 - wymagane uzasadnienie |
| `WypisNaWlasneZadanie_AnulujeOtwarteZleceniaIZapisujePowod` | BR-10/BR-11 |
| `PrzekazanieNaOddzial_BezOddzialu_JestZablokowane` | BR-11 - wymagany oddział przyjmujący |
| `PrzekazanieNaOddzial_BezPowodu_JestZablokowane` | BR-11 |
| `PrzekazanieNaOddzial_UstawiaStanPrzekazanyIZapisujeOddzial` | BR-11 - stan `TransferredOut` |
| `PonownyWypis_JestZablokowany` | Terminalność pobytu |
| `LekarzWypisujePacjentaPoZakonczeniuLeczenia` | BR-13 - audyt `PatientDischarged` |
| `WypisZOtwartymZleceniem_JestOdrzuconyIAudytowany` | BR-25 - audyt `PatientDischargeBlocked` |
| `WypisNaWlasneZadanie_WymagaPowodu` | BR-11 |
| `PrzekazanieNaOddzial_WymagaIstniejacegoOddzialu` | BR-11 - walidacja oddziału |
| `KatalogOddzialowZawieraOddzialySzpitala` | Katalog danych referencyjnych |

Uruchomienie samych testów wypisu:

```powershell
dotnet test tests/SOR.Domain.Tests/SOR.Domain.Tests.csproj --filter "FullyQualifiedName~DischargeTests"
```

Pozostałe grupy: `CatalogDomainTests` (10), `CatalogWorkflowTests` (6),
`MedicationOrderWorkflowTests` (4).

## 5. Kluczowe decyzje implementacyjne

1. **Jawne dodawanie bytów potomnych** (`TriageAssessment`, `ZoneTransfer`, `MedicalOrder`,
   `PatientDischarge`) przez repozytoria — EF Core błędnie klasyfikował encje odkryte w nawigacji
   jako istniejące i próbował wykonać `UPDATE` nieistniejącego wiersza.
2. **Konwerter `DateTimeOffset`** — SQLite nie porównuje natywnie tego typu; zapis jako `long`
   pozwala na sortowanie i filtrowanie po stronie serwera.
3. **Token współbieżności `RowVersion`** nadawany w aplikacji (`IsConcurrencyToken` +
   `ValueGeneratedNever`), zamiast generowania przez magazyn, co jest nieobsługiwane przez SQLite.
4. **Odświeżanie kontekstu sesji po rotacji** (`RefreshCurrentUserContextAsync`) — widok
   natychmiast pracuje w nowej strefie (BR-16).
5. **Rozwiązanie cyklu DI** przez rozdzielenie odczytu obciążenia i obserwatora zdarzeń.
6. **Audyt w tej samej transakcji** co operacja biznesowa (BR-25).
7. **Cyfra kontrolna PESEL liczona wg rozporządzenia** (`PeselNumber.HasValidChecksum`):
   wagi `1,3,7,9,1,3,7,9,1,3`, do sumy wchodzi tylko ostatnia cyfra każdego iloczynu, cyfra
   kontrolna dopełnia sumę do dziesiątki (0, gdy ostatnia cyfra sumy to 0). Reguła „suma
   modulo 11" jest podobna, ale nie równoważna — przyjmowała numery nieistniejące
   (np. `85441410008`) i odrzucała poprawne (`02070803628`, `44051401458`). Walidacja obejmuje
   też datę zakodowaną w numerze oraz zgodność z płcią i datą podanymi w formularzu
   (`Patient.Register`).

## 6. Scenariusz demonstracyjny

1. Uruchom aplikację i zaloguj się jako `lekarz.trm` / `SOR2026!trm` — strefa to TRM (z grafiku).
2. Wprowadź wniosek o zmianę strefy na EMG z przyczyną „Napływ pacjentów urazowych”.
3. Zarejestruj pacjenta (PESEL 11 cyfr), wykonaj Triage np. `Red` — pacjent trafia do Części ratunkowej.
4. Otwórz kartę pacjenta dwukrotnym kliknięciem wiersza — okno **„Historia pobytu"** jest jedynym
   miejscem akcji: dodaj zlecenie (typ, opis, pilność) i ustaw rozpoznanie ICD-10 (`I21.4`).
5. Spróbuj wypisać pacjenta w tym samym oknie — przy **zakończeniu leczenia** system zgłosi blokadę
   (brak otwartych zleceń), a przy **przekazaniu na inny oddział** dodatkowo wymusi wybór oddziału
   i uzasadnienia.
6. Wybierz scenariusz wypisu i zatwierdź przyciskiem **Wypisz pacjenta z SOR** — karta zmienia stan
   na „Wypisany z SOR” (przekazanie: „Przekazany na inny oddział”), a wpis trafia do historii wypisów.
7. Zaloguj się jako `ordynator`, aby zobaczyć wszystkie strefy i zlecić rotację innemu pracownikowi.
8. Zaloguj się jako `ratownik.trm` / `SOR2026!rtrm` — w oknie historii oznacz zlecenie
   podania leku lub badania obrazowego przyciskiem **Wykonane**; zlecenie laboratoryjne,
   zabiegu i konsultacji pozostaje niedostępne, a formularz dodawania zlecenia oraz przycisk
   wypisu i rozpoznania są zablokowane.

### Wypis pacjenta z SOR (BR-11)

| Scenariusz | Warunki | Efekt |
|---|---|---|
| Zakończenie leczenia | rozpoznanie ICD-10, brak otwartych zleceń | `PatientState.Closed` |
| Wypis na własne żądanie | uzasadnienie; otwarte zlecenia zostają anulowane | `PatientState.Closed` |
| Przekazanie na inny oddział | rozpoznanie ICD-10, brak otwartych zleceń, oddział przyjmujący, uzasadnienie | `PatientState.TransferredOut` |

Katalog oddziałów (26 pozycji) odwzorowuje kliniki i oddziały Szpitala Uniwersyteckiego nr 1
im. dr. Antoniego Jurasza w Bydgoszczy (`jurasza.umk.pl/kliniki`) i służy wyłącznie do oznaczenia
oddziału przyjmującego — nie jest wykazem dostępności łóżek. Wypis rejestruje lekarz lub
koordynator; każda próba (udana lub odrzucona) trafia do dziennika audytu.

## 7. Ekran po zalogowaniu

Układ `MainWindow.xaml` po zalogowaniu jest podzielony na cztery poziomy (sekcje A–D specyfikacji
interfejsu) przełączane przez `WorkspaceSection` w `MainViewModel`:

| Sekcja | Zawartość | Przełączanie |
|---|---|---|
| **PRZYJĘCIA** (domyślna) | tabela pobytów, raport z dyżuru, badania obrazowe | zakładka modułu, menu `Moduły`, pasek ikon |
| **PORADNIA** | ekran informacyjny — modułu brak w modelu domenowym | zakładka modułu, menu `Moduły` |
| **INNE** | dotychczasowy pulpit strefy (trzy kolumny) oraz zakładka „Formularz leków” | zakładka modułu, menu `Moduły` |

| Poziom | Zawartość |
|---|---|
| A — pasek modułów | `PRZYJĘCIA`, `PORADNIA`, `INNE` (RadioButton, grupa `Moduły`, aktywna zakładka na białym tle) oraz po prawej `LOGIN NAZWISKO_IMIĘ (ROLA)`, `Strefa: …` i przełącznik motywu |
| A — pasek operacyjny | `Szukaj`, `Karta pobytu`, `Raport`, `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia`, `KMCR`, `Wyjdź` — ikony rysowane wektorowo (`Path`), bez bibliotek ikon |
| A — menu | `Plik`, `Moduły`, `Widok` (te same przełączenia co zakładki modułów) |
| B — jednostka i wyszukiwarka | jednostka organizacyjna (domyślnie SOR), pole pacjenta z lupą, przycisk `Szukaj` |
| C — zakres dat i filtry | dwa wiersze: `Data od` i `Data do`, każdy z sześcioma przyciskami (`◀ dzień`, `dzień ▶`, `◀ miesiąc`, `miesiąc ▶`, `◀ rok`, `rok ▶`); pola dat pokazują pełny zapis `05.10.2026`; niżej siatka filtrów statusowych oraz `Brak karty odmowy` w kolejnym wierszu |
| D — dane | tabela z kolumnami: `COVID-19`, `Pacjent`, `Triage`, `Objawy`, `Czas`, `Data przyjęcia`, `Data wypisu`, `Nr`, `TOPSOR`, `Oddział`, `Księga główna`, `Ubezp.`, `Onkologiczna`, `Zgoda`, `Kat.`, `IDH` |
| pasek statusu | komunikat listy, licznik `Pobytów w zakresie: X / Y`, komunikat modułu bez danych, komunikat pulpitu i `Odśwież pulpit` |

Pasek operacyjny jest deklaratywnym odnośnikiem do modułów: `Karta pobytu` i `Raport` przełączają
zakładkę danych, `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia` i `KMCR` wyświetlają
komunikat o braku modułu (bez udawania danych), a `Wyjdź` kończy sesję. Ikony mają
`AutomationProperties.Name`, co umożliwia testy interfejsu przez UI Automation.

## 8. Moduł przyjęć — karta pobytu, raport z dyżuru i badania obrazowe

### Źródło danych

`IPatientStayService` (`src/SOR.Application/Services/PatientStayService.cs`) realizuje wyłącznie
odczyt i projekcję. Zapytanie `StayQuery(FromUtc, ToUtc, Filter, WithoutRefusalCard)` trafia do
`IPatientRepository.GetStaysAsync`, który dołącza `Include(p => p.Orders)` — bez tego zlecenia
obrazowe nie byłyby widoczne w zakładce „Badania obrazowe”. Serwis wyprowadza kolumny widokowe:
numer dokumentacji `MRN-XXXXXXXX`, krótki numer pacjenta `PAC-XXXXXX`, wiek w latach oraz kategorię
pilności z oceny Triage. Zapytania nie są wykonywane bez zalogowanej sesji (`SessionStateChanged`
odświeża listę po zalogowaniu i wylogowaniu).

### Kolumny listy pobytów

| Kolumna | Źródło |
|---|---|
| `COVID-19` | flaga `Patient.Covid19` (przyjęcie z powodu COVID-19) |
| `Pacjent` | `PESEL, PAC-XXXXXX, Nazwisko, Imię, Wiek lat` (złożona kolumna `PatientDisplay`) |
| `Triage`, `Kat.` | ostatnia ocena Triage (`TriageCategory`) i wyprowadzona z niej kategoria pilności |
| `Objawy` | objawy zgłoszone przy rejestracji |
| `Czas` | czas pobytu w strefie |
| `Data przyjęcia`, `Data wypisu`, `Nr` | daty przyjęcia/wypisu oraz kolejny numer pobytu pacjenta w SOR (`Patient.VisitNumber`) |
| `TOPSOR` | numer dokumentacji medycznej (`MRN-…`) |
| `Oddział` | strefa pobytu albo oddział przekazania |
| `Księga główna`, `Ubezp.`, `Onkologiczna`, `Zgoda`, `IDH` | dane administracyjne pacjenta (`Patient.MainLedgerBook`, `HasInsurance`, `IsOncological`, `ConsentGiven`, `IdhNumber`); brak wartości → `—` |

### Filtry

Filtry odpowiadają radio buttonom panelu C. Wartości bez odpowiednika w modelu domenowym
zwracają **pustą listę** zamiast danych, których system nie przechowuje.

| Filtr (`StayFilter`) | Zachowanie |
|---|---|
| `All` | wszyscy pacjenci z zakresu dat — łącznie z wypisanymi i przeniesionymi (domyślnie) |
| `CurrentlyInHospital` | wszyscy aktualnie obecni w SOR, także nieztriagowani |
| `TransferredToDepartment` | wypis typu przekazanie |
| `Cancelled` | wypis na własne żądanie |
| `CurrentlyInBay` | pacjenci aktualnie przypisani do strefy; zakres strefy pochodzi z sesji
(strefa bieżącego użytkownika, koordynator — wszystkie strefy), przekazywany przez
`StayQuery.ZoneScopeId` |
| `Covid19` | pacjenci z flagą `Patient.Covid19` — filtrowanie w `PatientStayService.Matches` oraz ponowne zawężenie po stronie widoku |
| `Refused`, `NoInsurance`, `DischargedWithoutFormalities`, `LedgerVerification`, `Brak karty odmowy` | brak odpowiednika w modelu — pusty wynik |

### Raport z dyżuru i badania obrazowe

- **Raport z dyżuru** — `IAuditLogService.GetRecentAsync()` (ostatnie wpisy dziennika audytu:
  czas, użytkownik, akcja, opis).
- **Badania obrazowe** — projekcja `MedicalOrder` typu `Imaging` wraz z `Patient` (wyłącznie
  zlecenia wystawione w zakresie dat), ze statusem zmapowanym na `Nowe`, `W realizacji`,
  `Zrealizowane` i `Anulowane`.

### Dane startowe

Domyślny zakres dat to **05.10.2026**, a dane startowe (`DatabaseSeeder`) obejmują dyżur
**04.10.2026** (12 pobytów). Pierwsze uruchomienie pokazuje więc pustą tabelę — poprawny wynik
zapytania; przycisk `◀ dzień` przy `Data od` przełącza zakres na dzień z danymi. Świadomie nie
zmieniono domyślnej daty wymaganej przez specyfikację interfejsu. Seeder wariantuje też dane
administracyjne kolumn listy — deterministycznie wg indeksu pacjenta na liście (nie losowo):
Ubezp. `true`/`false`/brak, Zgoda `true`/`false`/brak, Onkologiczna u dwóch pacjentów,
Księga główna `KG-…` u części, `IDH-…` u części, COVID-19 u dwóch pacjentów, `Nr` 1–3.

### Historia pobytu (dwukrotne kliknięcie wiersza pacjenta)

Dwukrotne kliknięcie wiersza pacjenta — zarówno na liście pobytów (zakładka „Karta pobytu"), jak
i na pulpicie strefy (kolejka Triage oraz lista pacjentów strefy) — otwiera modalne okno
**„Historia pobytu"** (`PatientHistoryWindow`) z pełnym odtworzeniem przebiegu pobytu w SOR.

| Element | Zawartość |
|---|---|
| Nagłówek | imię i nazwisko, PESEL, wiek, płeć, stan karty, czas i strefa przyjęcia, triage, rozpoznanie ICD-10, zgłaszane dolegliwości, blokady wypisu |
| Oceny Triage | pełna historia ocen (kolejna ocena nie nadpisuje poprzedniej), z uzasadnieniem i parametrami życiowymi |
| Przeniesienia | strefa źródłowa i docelowa, uzasadnienie, czas |
| Procedury i badania | zlecenia wg typu (`MedicalOrderType`), opis, stan realizacji, pilność, czas wykonania |
| Podane leki | preparat, dawka, droga podania, czas, uwagi |
| Wypisy z SOR | sposób zakończenia pobytu (`DischargeType`), oddział, uzasadnienie, osoba rejestrująca |
| Akcje | formularz nowego zlecenia (typ, opis, pilność), przycisk **Wykonane** przy zleceniu, lista pakietów medycznych z wyszukiwaniem i przyciskiem **Zastosuj pakiet**, lista rozpoznań ICD-10 z wyszukiwaniem po kodzie i nazwie, a także wypis pacjenta — widoczne zależnie od uprawnień (`CanIssueOrders`, `CanExecuteOrders`, `CanEnterDiagnosis`, `CanDischargePatient`) |

Dane pobiera `IPatientService.GetPatientAsync` przez `PatientRepository.GetByIdAsync`, który
dołącza `TriageHistory` i `Discharges` (bez tych `Include` historia ocen i wypisów byłaby pusta).
Warstwę widoku tworzy `PatientHistoryViewModel` — oprócz podglądu udostępnia formularze zlecenia,
wyboru rozpoznania ICD-10, zastosowania pakietu i wypisu. Pakiety pobiera `IMedicalBundleService`
(`GetAllAsync`/`SearchAsync`), a rozpoznania i ich zapis `IIcd10CatalogService`
(`GetEmergencyRelevantAsync`/`SearchAsync`/`AssignAsync` — wyszukiwanie obejmuje kod i opis);
listę oddziałów dostarcza `IDepartmentCatalogService`. Zapis wykonują metody serwisów
aplikacyjnych (każda commituje samodzielnie), a `IPatientHistoryDialogService.ShowAsync` zwraca
`true`, gdy dane uległy zmianie, dzięki czemu lista pacjentów odświeża się po zamknięciu okna.
Okno otwiera `IPatientHistoryDialogService` — dzięki temu listy pacjentów nie tworzą okien
bezpośrednio i pozostają testowalne. Zdarzenie dwukliku obsługuje `MainWindow.xaml.cs`
(`MouseDoubleClick`), ponieważ `DataGrid` nie udostępnia wiązania wyzwalanego dwuklikiem;
kliknięcia nagłówka i paska przewijania są ignorowane (wykrywanie wiersza przez `VisualTreeHelper`).
Każda kolumna daty ma własność `*Display` w formacie `dd.MM.yyyy HH:mm` (strefa lokalna).

## 9. Motyw kolorystyczny i tryb ciemny

`ThemeService` podmienia w `Application.Resources` słownik `Themes/Light.xaml` lub
`Themes/Dark.xaml` i zapisuje wybór w `%LOCALAPPDATA%\SOR\ui-theme.json`, dlatego przełączenie
motywu działa w locie i jest trwałe między uruchomieniami.

**Zasada konwencji:** każdy styl w `App.xaml` korzysta wyłącznie z `DynamicResource` wskazujących
pędzle motywu (`AppBackgroundBrush`, `SurfaceBrush`, `TextPrimaryBrush`, `AccentBrush`,
`PanelPink*`, `PopupOverlayBrush`). Wstawienie literalnego koloru w szablonie oznacza, że kontrolka
nie przemaluje się przy zmianie motywu.

Kontrolki przebudowane pod tryb ciemny (domyślne szablony WPF są jasne — w trybie ciemnym
dawały jasne tło pod jasnym tekstem):

| Kontrolka | Rozwiązanie |
|---|---|
| `TabControl` / `TabItem` | `TabItem` to wyłącznie nagłówek zakładki (różowe tło, aktywna biała z obwódką akcentu), `TabControl` rysuje pasek zakładek i pole treści na pędzlach motywu |
| `ComboBox` / `ComboBoxItem` | własny szablon z polem, strzałką i listą; pokrywa `PopupOverlayBrush` przygasza własne okno, dzięki czemu lista jest czytelna w obu motywach |
| `ListBox` / `ListBoxItem` | tło, wyróżnienie i pozycja aktywna na pędzlach motywu |
| `DatePicker` / `Calendar` / `CalendarItem` / `CalendarDayButton` | kalendarz w kolorach motywu (nagłówek, „dziś”, zaznaczenie, dni nieaktywne) oraz pełna data `dd.MM.yyyy` w polu (patrz „Format dat” poniżej) |
| `DataGrid`, `DataGridColumnHeader`, `DataGridCell` | nagłówki i wiersze przemalowywane ręcznie, bo domyślny szablon używa zasobów systemowych |
| `Button`, `TextBox`, `PasswordBox`, `CheckBox`, `Expander`, `ProgressBar` | spójne kolory motywu i stany `:hover` / `:disabled` |

**Format dat:** `App.OnStartup` wymusza kulturę `pl-PL` (`DefaultThreadCurrentCulture`,
`CurrentCulture`) i wzorzec `dd.MM.yyyy`, a styl `DatePicker` ustawia
`SelectedDateFormat="Short"`. Dzięki temu pole daty pokazuje zawsze pełny zapis
`05.10.2026` — dzień, miesiąc i rok — niezależnie od ustawień regionalnych maszyny, a wpisany
tekst jest poprawnie wczytywany. Sama wartość `SelectedDateFormat` przyjmuje wyłącznie
`Long` i `Short` (nie jest to wzorzec formatujący), dlatego polski zapis z zerami wiodącymi
zapewnia kultura aplikacji. Kontrast pola daty wynosi 15,7:1 w motywie jasnym i 13,3:1
w ciemnym.

**Pułapka szablonu zakładek:** treść aktywnej zakładki musi być prezentowana przez `TabControl`
(`ContentPresenter` o nazwie `PART_SelectedContentHost` z `ContentSource="SelectedContent"`).
Umieszczenie `ContentPresenter` z `ContentSource="Content"` w szablonie `TabItem` renderuje
treść wewnątrz nagłówka (podwójne renderowanie i rozjechana wysokość zakładki), ponieważ WPF
przenosi zawartość `TabItem` do obszaru treści `TabControl`.

Weryfikacja zmian motywu: `dotnet build`, `dotnet test` (112 testów) oraz skrypt UI Automation
sprawdzający przełączenie motywu, przełączanie zakładek, rozwinięcie `ComboBox` i kalendarza
`DatePicker` w obu motywach.

## 10. Znane ograniczenia

- Brak migracji EF (schemat tworzony przez `EnsureCreatedAsync`) — `DatabaseInitializer`
  automatycznie odtwarza schemat po wykryciu brakującej tabeli lub kolumny; pozostałe zmiany
  modelu (np. zmiana typów) wymagają ręcznego usunięcia pliku bazy.
- Monitoring nie działa jako trwała pętla w tle w UI (interfejs udostępnia `StartMonitoringAsync`).
- Moduł **PORADNIA** nie jest zaimplementowany — brak modelu domenowego, sekcja pokazuje
  komunikat zastępczy (analogicznie `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia`, `KMCR`
  w pasku operacyjnym).
- Filtry bez odpowiednika w modelu (Odmowa, Brak ubezpieczenia,
  Wypisani bez proc. rol. a w izbie, Kategorie – weryfikacja, Brak karty odmowy) pokazują
  pusty wynik. Kolumny COVID-19, Księga główna, Ubezp., Onkologiczna, Zgoda i IDH to dane
  administracyjne pacjenta (`Patient`) — brak wartości pokazywany jako `—`.
- Brak danych startowych dla domyślnego zakresu dat (05.10.2026) — dane seed dotyczą 04.10.2026.
- Brak automatycznej weryfikacji wizualnej w CI (WPF wymaga sesji pulpitu) — kontrast motywów
  sprawdzany jest ręcznie i przez UI Automation.
- Brak integracji z systemami zewnętrznymi (HIS, laboratorium) — poza zakresem.

## 11. Zmiany — Dark mode i poprawki UI (commit `eb3a27f`)

**Motyw i pasek tytułu:**
- Dodano `Services/WindowTitleBarTheme.cs` — ustawia natywnie ciemny pasek tytułu okna przez DWM
  (`DWMWA_USE_IMMERSIVE_DARK_MODE`, fallback na starszy atrybut) i wymusza przerysowanie ramki
  (`SetWindowPos` z `SWP_FRAMECHANGED`). Wywoływane po `window.Show()` oraz przy zmianie motywu
  (`App.xaml.cs`).
- Dodano pędzel `InputBorderBrush` do `Themes/Dark.xaml` i `Themes/Light.xaml`.
- Okno „Historia pobytu” (otwierane po kliknięciu pacjenta) barwi swój natywny pasek tytułowy
  tak samo jak okno główne: `PatientHistoryDialogService` wywołuje `WindowTitleBarTheme.Apply`
  przed `ShowDialog()` i dopisuje się do `IThemeService.ThemeChanged` na czas otwartego dialogu
  (odpisuje w `finally`). Treść okna korzysta z poziomych stylów `App.xaml`, więc przełączenie
  motywu przemalowuje okno również w trakcie wyświetlania.

**DatePicker:**
- `DatePicker.BorderBrush` korzysta z `{DynamicResource InputBorderBrush}`, `BorderThickness=1`.
- Własny `ControlTemplate` dla `DatePickerTextBox` (przezroczyste tło, brak obramowania, zachowane
  `PART_ContentHost`, `PART_Watermark` i grupa stanów `WatermarkStates`) — usuwa twardo zakodowane
  białe obramowanie z domyślnego szablonu Aero2.

**Kalendarz (popup DatePicker):**
- Dodano nazwane style motywowe: `ThemedCalendarDayButton`, `ThemedCalendarButton`,
  `ThemedCalendarItem`, `ThemedCalendarStyle`.
- Dodano niejawny styl `Calendar`, wiążący `CalendarDayButtonStyle`, `CalendarButtonStyle`
  i `CalendarItemStyle` z powyższymi zasobami (zachowując powiązanie `DatePicker.CalendarStyle`).
- Przełączanie widoku miesiąc/rok/dekada obsługiwane przez `DataTrigger` na `Calendar.DisplayMode`.
- Ograniczenia zakresu: „Data od” ustawia `DisplayDateEnd = DateTo`, „Data do” ustawia
  `DisplayDateStart = DateFrom`.

**ComboBox:**
- `ControlTemplate` `ComboBox` uzupełniony o `ContentTemplateSelector`, `VerticalContentAlignment`
  i `HorizontalContentAlignment` w prezenterze wybranego elementu (poprawne renderowanie
  `DisplayMemberPath`/`ItemTemplate` w zamkniętym polu).

**Zakres dat (lista pobytów):**
- Podział przycisków krokowych na 12 komend: `Previous/Next{Day,Month,Year}{From,To}` — osobne dla
  „Data od” i „Data do”.
- Strażniki niezmiennika `DateFrom <= DateTo`: zwiększenie `DateFrom` powyżej `DateTo` oraz
  zmniejszenie `DateTo` poniżej `DateFrom` nie dają efektu (przyciski pozostają aktywne).
- Settery `DateFrom`/`DateTo` normalizują wartość do granicy zakresu i wymuszają `OnPropertyChanged`,
  gdy znormalizowana wartość jest równa bieżącej (pozwala wycofać nieprawidłowy wpis w `DatePicker`).
- Wiązania w `MainWindow.xaml`: wiersz „od” używa komend `From` oraz
  `DisplayDateEnd="{Binding Stays.DateTo}"`, wiersz „do” używa komend `To` oraz
  `DisplayDateStart="{Binding Stays.DateFrom}"`.

**AMK → KMCR:**
- Zaktualizowano etykiety, tooltipy i `AutomationProperties.Name` w `MainWindow.xaml` oraz nazwę
  komendy na `ShowKmcrCommand` w `MainViewModel` wraz z komunikatem modułu.

**„Inne → Formularz leków”:**
- Lewa kolumna opakowana w `ScrollViewer`, dostosowano rozkład wierszy w celu uniknięcia nakładania
  i obcinania sekcji.
- Dodano `MinHeight="140"` dla `DataGrid` formularza leków.
- `ComboBox` „Grupa Terapeutyczna” używa jawnego `ItemTemplate` (`TextBlock Text="{Binding Label}"`)
  zamiast `DisplayMemberPath`.
- Pakiety medyczne i katalog rozpoznań ICD-10 usunięto z tej zakładki i przeniesiono do okna
  „Historia pobytu”; zakładkę przemianowano z „Formularz i pakiety” na „Formularz leków”.

## 12. Zmiany — dane administracyjne kolumn listy i ciemne tło historii

**Dane administracyjne w modelu (`Patient`):**
- Encja `Patient` zyskała pola odpowiadające kolumnom listy pobytów: `VisitNumber` (Nr, domyślnie 1),
  `Covid19`, `HasInsurance` (Ubezp.), `IsOncological` (Onkologiczna), `ConsentGiven` (Zgoda),
  `MainLedgerBook` (Księga główna), `IdhNumber` (IDH). Wartości ustawia metoda domenowa
  `SetAdministrativeAttributes(...)` z walidacją: karta musi być otwarta, numer pobytu ≥ 1,
  kody administracyjne najwyżej 16 znaków (puste = brak danych).
- `PatientStayService.ToDto` mapuje realne wartości zamiast placeholderów (`false` / `—` / `null`);
  kolumna `Nr` czyta `Patient.VisitNumber` — usunięto `BuildVisitNumbers`, która z definicji
  zwracała zawsze 1 (po jednej encji na pacjenta, PESEL unikalny).
- `DatabaseSeeder` wariantuje dane administracyjne deterministycznie wg indeksu pacjenta
  (Ubezp. true/false/brak, Zgoda true/false/brak, Onkologiczna u dwóch, Księga `KG-…` u części,
  IDH u części, COVID-19 u dwóch, `Nr` 1–3).
- `DatabaseInitializer` wykrywa brakujące kolumny (`PRAGMA table_info` vs model) i odtwarza
  schemat z reseedem — dodanie kolumny do istniejącej tabeli nie wymaga już ręcznego
  usuwania pliku `sor.db`.

**Filtr COVID-19:**
- `StayFilter.Covid19` działa: `PatientStayService.Matches` zawęża po `Patient.Covid19`,
  widok (`PatientStayListViewModel.ApplyFilters`) ponownie filtruje po stronie klienckiej;
  usunięto go z listy filtrów bez odpowiednika w modelu.

**Ciemne tło okna „Historia pobytu”:**
- `PatientHistoryWindow.xaml` ustawia jawnie `Background="{DynamicResource AppBackgroundBrush}"`
  i `Foreground="{DynamicResource TextPrimaryBrush}"` na elemencie `<Window>` — niejawny styl
  `TargetType="Window"` z `App.xaml` nie dociera do okna pochodnego, więc bez tego okno
  renderowało się na jasno mimo ciemnego motywu (wraz z naprawą paska tytułowego z §11).
