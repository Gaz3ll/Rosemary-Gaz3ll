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
  i otwiera `MainWindow`. Obsługuje też globalne wyjątki.
- **`MainViewModel`** — punkt kompozycji widoku (łączy `SessionViewModel` i `PatientBoardViewModel`).
- **`SessionViewModel`** — logowanie, kontekst strefy, wniosek o zmianę strefy, rekomendacje rotacji.
- **`PatientBoardViewModel`** — rejestracja, Triage, kolejki pacjentów, karta pacjenta, zlecenia,
  rozpoznanie, blokada i zamknięcie karty, obciążenie strefy.
- **`MainWindow.xaml`** — ekran logowania oraz pulpit strefy (trzy kolumny + baner rekomendacji
  i pasek statusu). Konwertery `BooleanToVisibilityConverter` / `InverseBooleanToVisibilityConverter`
  / `StringToVisibilityConverter` utrzymują widok deklaratywnym.
- **`UiThreadDispatcher`** — marshaling operacji na wątek UI.
- **`ExceptionMessageMapper`** — tłumaczenie wyjątków na komunikaty dla użytkownika.

Bindingi do właściwości tylko do odczytu (`ProgressBar.Value`, kolumny `DataGrid`) są jawnie
`Mode=OneWay`, aby uniknąć wyjątku „a TwoWay binding cannot work on a read-only property”.

## 4. Testy

Łącznie **92 testy** (xUnit), wszystkie przechodzą. Dzielą się na cztery grupy.

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
| `RatownikNieZamykaKartyBezPotwierdzonegoTransportu` | BR-09/BR-13 |
| `LekarzNadalRealizujeKazdeZlecenie` | Brak regresji dla lekarza |
| `SeedUzupelniaKontaRatownikowWJuzyIstniejacejBazie` | Uzupełnianie bazy sprzed dodania roli |

## 5. Kluczowe decyzje implementacyjne

1. **Jawne dodawanie bytów potomnych** (`TriageAssessment`, `ZoneTransfer`) przez repozytoria —
   EF Core błędnie klasyfikował encje odkryte w nawigacji jako istniejące i próbował wykonać
   `UPDATE` nieistniejącego wiersza.
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
5. Spróbuj zamknąć kartę — system zgłosi blokady (otwarte zlecenia / brak transportu).
6. Zaloguj się jako `ordynator`, aby zobaczyć wszystkie strefy i zlecić rotację innemu pracownikowi.
7. Zaloguj się jako `ratownik.trm` / `SOR2026!rtrm` — otwórz kartę pacjenta i oznacz zlecenie
   podania leku lub badania obrazowego przyciskiem **Wykonane**; zlecenie laboratoryjne,
   zabiegu i konsultacji pozostaje niedostępne, a formularz dodawania zlecenia jest zablokowany.

## 7. Znane ograniczenia

- Brak migracji EF (schemat tworzony przez `EnsureCreatedAsync`) — zmiany modelu wymagają
  usunięcia pliku bazy.
- Monitoring nie działa jako trwała pętla w tle w UI (interfejs udostępnia `StartMonitoringAsync`).
- Brak integracji z systemami zewnętrznymi (HIS, laboratorium) — poza zakresem.
