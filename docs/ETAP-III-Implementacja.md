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
przez `DatabaseSeeder` i operacja jest idempotentna.

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
- **`PatientBoardViewModel`** — rejestracja, Triage, kolejki pacjentów, karta pacjenta, zlecenia,
  rozpoznanie, blokada i zamknięcie karty, obciążenie strefy.
- **`PatientStayListViewModel`** — ekran po zalogowaniu: jednostka organizacyjna i wyszukiwarka,
  zakres dat z nawigacją krokową, filtry statusowe, wiersze tabeli, raport z dyżuru i badania
  obrazowe (patrz [rozdział 8](#8-moduł-przyjęć--karta-pobytu-raport-z-dyżuru-i-badania-obrazowe)).
- **`MedicationCatalogViewModel`** — katalog leków, pakiety medyczne i formularz zleceń.
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

Łącznie **106 testów** (xUnit), wszystkie przechodzą. Dzielą się na pięć grup.

### `PersistenceTests` — trwałość (5)

| Test | Co weryfikuje |
|---|---|
| `EnsureCreated_TworzyPoprawnySchemat_BezBledowModelu` | Poprawność mapowania EF i utworzenie schematu |
| `Seed_JestIdempotentny_NieDuplikujeDanych` | Idempotencja danych startowych |
| `DiagnozaIcd10_JestZapisanaWRepozytorium` | Zapis rozpoznania ICD-10 |
| `UzasadnienieZmianyStrefy_JestZapisaneWRepozytorium` | Zapis uzasadnienia rotacji |
| `DziennikAudytu_ZapisujeNieudanaProbeLogowania` | Audyt nieudanej próby logowania |

### `ScenarioTests` — scenariusze integracyjne (10)

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

## 6. Scenariusz demonstracyjny

1. Uruchom aplikację i zaloguj się jako `lekarz.trm` / `SOR2026!trm` — strefa to TRM (z grafiku).
2. Wprowadź wniosek o zmianę strefy na EMG z przyczyną „Napływ pacjentów urazowych”.
3. Zarejestruj pacjenta (PESEL 11 cyfr), wykonaj Triage np. `Red` — pacjent trafia do Części ratunkowej.
4. Otwórz kartę pacjenta: dodaj zlecenie, ustaw rozpoznanie ICD-10 (`I21.4`).
5. Spróbuj wypisać pacjenta — przy **zakończeniu leczenia** system zgłosi blokadę (brak otwartych
   zleceń), a przy **przekazaniu na inny oddział** dodatkowo wymusi wybór oddziału i uzasadnienia.
6. Wybierz scenariusz wypisu i zatwierdź przyciskiem **Wypisz pacjenta z SOR** — karta zmienia stan
   na „Wypisany z SOR” (przekazanie: „Przekazany na inny oddział”), a wpis trafia do historii wypisów.
7. Zaloguj się jako `ordynator`, aby zobaczyć wszystkie strefy i zlecić rotację innemu pracownikowi.
8. Zaloguj się jako `ratownik.trm` / `SOR2026!rtrm` — otwórz kartę pacjenta i oznacz zlecenie
   podania leku lub badania obrazowego przyciskiem **Wykonane**; zlecenie laboratoryjne,
   zabiegu i konsultacji pozostaje niedostępne, formularz dodawania zlecenia oraz przycisk
   wypisu są zablokowane.

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
| **INNE** | dotychczasowy pulpit strefy (trzy kolumny) oraz zakładka „Formularz i pakiety” | zakładka modułu, menu `Moduły` |

| Poziom | Zawartość |
|---|---|
| A — pasek modułów | `PRZYJĘCIA`, `PORADNIA`, `INNE` (RadioButton, grupa `Moduły`, aktywna zakładka na białym tle) oraz po prawej `LOGIN NAZWISKO_IMIĘ (ROLA)`, `Strefa: …` i przełącznik motywu |
| A — pasek operacyjny | `Szukaj`, `Karta pobytu`, `Raport`, `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia`, `AMK`, `Wyjdź` — ikony rysowane wektorowo (`Path`), bez bibliotek ikon |
| A — menu | `Plik`, `Moduły`, `Widok` (te same przełączenia co zakładki modułów) |
| B — jednostka i wyszukiwarka | jednostka organizacyjna (domyślnie SOR), pole pacjenta z lupą, przycisk `Szukaj` |
| C — zakres dat i filtry | dwa wiersze: `Data od` i `Data do`, każdy z sześcioma przyciskami (`◀ dzień`, `dzień ▶`, `◀ miesiąc`, `miesiąc ▶`, `◀ rok`, `rok ▶`); pola dat pokazują pełny zapis `05.10.2026`; niżej siatka filtrów statusowych oraz `Brak karty odmowy` w kolejnym wierszu |
| D — dane | tabela z kolumnami: `COVID-19`, `Pacjent`, `Triage`, `Objawy`, `Czas`, `Data przyjęcia`, `Data wypisu`, `Nr`, `TOPSOR`, `Oddział`, `Księga główna`, `Ubezp.`, `Onkologiczna`, `Zgoda`, `Kat.`, `IDH` |
| pasek statusu | komunikat listy, licznik `Pobytów w zakresie: X / Y`, komunikat modułu bez danych, komunikat pulpitu i `Odśwież pulpit` |

Pasek operacyjny jest deklaratywnym odnośnikiem do modułów: `Karta pobytu` i `Raport` przełączają
zakładkę danych, `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia` i `AMK` wyświetlają
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
| `COVID-19` | brak odpowiednika w modelu — `—` |
| `Pacjent` | `PESEL, PAC-XXXXXX, Nazwisko, Imię, Wiek lat` (złożona kolumna `PatientDisplay`) |
| `Triage`, `Kat.` | ostatnia ocena Triage (`TriageCategory`) i wyprowadzona z niej kategoria pilności |
| `Objawy` | objawy zgłoszone przy rejestracji |
| `Czas` | czas pobytu w strefie |
| `Data przyjęcia`, `Data wypisu`, `Nr` | daty przyjęcia/wypisu i kolejny numer pobytu pacjenta w SOR |
| `TOPSOR` | numer dokumentacji medycznej (`MRN-…`) |
| `Oddział` | strefa pobytu albo oddział przekazania |
| `Księga główna`, `Ubezp.`, `Onkologiczna`, `Zgoda`, `IDH` | brak odpowiedników w modelu — `—` |

### Filtry

Filtry odpowiadają radio buttonom panelu C. Wartości bez odpowiednika w modelu domenowym
zwracają **pustą listę** zamiast danych, których system nie przechowuje.

| Filtr (`StayFilter`) | Zachowanie |
|---|---|
| `All` | wszyscy pacjenci z zakresu dat (domyślnie) |
| `CurrentlyInHospital` | pacjenci w trakcie pobytu w SOR |
| `TransferredToDepartment` | wypis typu przekazanie |
| `Cancelled` | wypis na własne żądanie |
| `CurrentlyInBay` | pacjenci oczekujący w strefie segregacji (TRI) |
| `Refused`, `Covid19`, `NoInsurance`, `DischargedWithoutFormalities`, `LedgerVerification`, `Brak karty odmowy` | brak odpowiednika w modelu — pusty wynik |

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
zmieniono domyślnej daty wymaganej przez specyfikację interfejsu.

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

Weryfikacja zmian motywu: `dotnet build`, `dotnet test` (106 testów) oraz skrypt UI Automation
sprawdzający przełączenie motywu, przełączanie zakładek, rozwinięcie `ComboBox` i kalendarza
`DatePicker` w obu motywach.

## 10. Znane ograniczenia

- Brak migracji EF (schemat tworzony przez `EnsureCreatedAsync`) — zmiany modelu wymagają
  usunięcia pliku bazy.
- Monitoring nie działa jako trwała pętla w tle w UI (interfejs udostępnia `StartMonitoringAsync`).
- Moduł **PORADNIA** nie jest zaimplementowany — brak modelu domenowego, sekcja pokazuje
  komunikat zastępczy (analogicznie `Konsultacje`, `Wniosek`, `Przedstawiciel / Uprawnienia`, `AMK`
  w pasku operacyjnym).
- Filtry i kolumny bez odpowiednika w modelu (COVID-19, Odmowa, Brak ubezpieczenia,
  Wypisani bez proc. rol. a w izbie, Kategorie – weryfikacja, Brak karty odmowy, Księga główna,
  Ubezp., Onkologiczna, Zgoda, IDH) pokazują pusty wynik lub `—`, zamiast danych zastępczych.
- Brak danych startowych dla domyślnego zakresu dat (05.10.2026) — dane seed dotyczą 04.10.2026.
- Brak automatycznej weryfikacji wizualnej w CI (WPF wymaga sesji pulpitu) — kontrast motywów
  sprawdzany jest ręcznie i przez UI Automation.
- Brak integracji z systemami zewnętrznymi (HIS, laboratorium) — poza zakresem.
