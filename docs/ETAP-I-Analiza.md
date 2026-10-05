# ETAP I — Analiza wymagań i model dziedziny

## 1. Cel i zakres systemu

**SOR — System Obsługi Ratunkowej** to aplikacja desktopowa (WPF / .NET 9) wspierająca pracę
Szpitalnego Oddziału Ratunkowego. System realizuje cztery obszary:

1. **Logowanie kontekstowe** — po uwierzytelnieniu strefa robocza pracownika wynika z grafiku
   dyżurów, a nie z ręcznego wyboru.
2. **Segregacja medyczna (Triage)** — rejestracja pacjenta, nadanie kodu Triage i automatyczny
   przydział do strefy o odpowiednim profilu.
3. **Dynamiczna rotacja personelu** — monitoring obciążenia stref i wspomaganie decyzji
   o przesunięciu personelu, z pełnym audytem.
4. **Prowadzenie karty pacjenta** — zlecenia lekarskie, rozpoznanie ICD-10, blokada współbieżnej
   edycji i kontrolowane zamknięcie karty.

Zakres celowo pomija integracje z systemami zewnętrznymi (HIS, laboratorium) — są one
odwzorowane przez dane startowe i model domeny.

## 2. Aktorzy i role

| Rola (`UserRole`) | Opis | Uprawnienia |
|---|---|---|
| `Physician` | Lekarz SOR | Rozpoznanie ICD-10, zlecenia, operacje na pacjentach własnej strefy |
| `Nurse` | Pielęgniarka / ratownik triage | Rejestracja, Triage, wykonanie zleceń; brak rozpoznania |
| `Coordinator` | Koordynator / ordynator | Dostęp do wszystkich stref, rotacja innych pracowników, nadzór |
| `Paramedic` | Ratownik medyczny strefy klinicznej | Realizacja zleceń podania leku i badania obrazowego; brak wystawiania zleceń, rozpoznania i wypisu pacjenta |

Strefy oddziału (`ZoneKind`): `Triage` (TRI), `Emergency` (EMG), `Internal` (INT), `Trauma` (TRM).

## 3. Wymagania funkcjonalne

- **WF-01** Uwierzytelnianie z blokadą konta po 3 nieudanych próbach (60 s).
- **WF-02** Wyznaczenie strefy z aktywnego dyżuru; brak dyżuru → strefa awaryjna Triage.
- **WF-03** Rejestracja pacjenta z walidacją PESEL (11 cyfr, cyfra kontrolna wg rozporządzenia, zakodowana data urodzenia i zgodność płci).
- **WF-04** Triage w pięciostopniowej skali i przydział do strefy wg kodu.
- **WF-05** Monitoring obciążenia stref i wizualizacja statusu.
- **WF-06** Wniosek o zmianę strefy z obowiązkowym uzasadnieniem.
- **WF-07** Rekomendacja rotacji generowana przez silnik i przyjmowana przez kandydata/koordynatora.
- **WF-08** Zlecenia lekarskie i rozpoznanie ICD-10.
- **WF-09** Blokada karty i kontrolowany wypis z SOR: zakończenie leczenia (rozpoznanie ICD-10, brak otwartych zleceń), wypis na własne żądanie (uzasadnienie, anulacja zleceń) albo przekazanie na oddział szpitala.
- **WF-10** Dziennik audytu wszystkich istotnych operacji.

## 4. Reguły biznesowe (BR)

| Kod | Treść | Realizacja |
|---|---|---|
| BR-01 / BR-01a | Każdy pacjent musi przejść Triage; Kod Czerwony → Część ratunkowa | `Patient.AssignTriage`, `Patient.AssignToZone` |
| BR-02 | Strefa robocza wynika z grafiku dyżurów | `AuthenticationService.ResolveZoneContextAsync` |
| BR-03 | Rejestracja i Triage w module wstępnym | `PatientService.RegisterPatientAsync` / `PerformTriageAsync` |
| BR-04 | Kontrola zdolności przyjęciowej strefy | `Zone.EnsureCanAcceptPatient`, `StaffRotationService` |
| BR-05 | Manualna zmiana strefy tylko własnej, z uzasadnieniem | `StaffRotationService.ReassignZoneAsync`, `ReassignmentReason` |
| BR-06 / BR-06b | Klasyfikacja obciążenia; pacjent czerwony bez personelu = przeciążenie | `ZoneLoadCalculator`, `ZoneLoadThresholds` |
| BR-07 | Rekomendacja rotacji przyjmowana przez kandydata lub koordynatora | `RotationRecommendationEngine`, `AcceptRecommendationAsync` |
| BR-08 | Rotacja innych pracowników zarezerwowana dla koordynatora | `StaffRotationService.CoordinatorAssignAsync` |
| BR-09 | Wypis po zakończeniu leczenia wymaga rozpoznania ICD-10 | `Icd10Code`, `PatientDischargePolicy` |
| BR-10 | Wypis wymaga braku otwartych zleceń (anulowanych przy wypisie na własne żądanie) | `PatientDischargePolicy`, `Patient.Discharge` |
| BR-11 | Wypis z SOR: zakończenie leczenia, własne żądanie lub przekazanie na oddział | `DischargeType`, `PatientDischarge`, `Department`, `Patient.Discharge` |
| BR-12 | Pracownik operuje na własnej strefie; koordynator na wszystkich | `PatientService.EnsureStaffOfZoneOrCoordinator` |
| BR-13 | Rozpoznanie, zlecenie i wypis pacjenta tylko przez lekarza/koordynatora | `PatientService.SetDiagnosisAsync`, `AuthenticatedUserDto.CanDischargePatient` |
| BR-15 | Aktywność dyżuru w czasie | `DutyShift.IsActive` |
| BR-16 | Kontekst strefy z grafiku; odświeżany po rotacji | `AuthenticationService.RefreshCurrentUserContextAsync` |
| BR-17 | Brak nakładających się dyżurów w tej samej strefie | `DutyRoster.AddShift` |
| BR-18 | PESEL jako klucz biznesowy: 11 cyfr, cyfra kontrolna wg rozporządzenia (wagi `1,3,7,9,1,3,7,9,1,3`, do sumy tylko ostatnia cyfra iloczynu, cyfra dopełnia sumę do 10), zakodowana data urodzenia oraz zgodność płci z numerem | `PeselNumber`, `Patient.Register` |
| BR-19 | Zakaz przeniesienia pacjenta do tej samej strefy | `PatientService.TransferPatientAsync` |
| BR-20 | Blokada współbieżnej modyfikacji karty | `Patient.AcquireLock`, `RowVersion` |
| BR-25 | Audyt atomowy z operacją biznesową | `AuditLogService`, `IUnitOfWork` |

### Skala Triage (`TriagePolicy`)

| Kod | Waga obciążenia | Maks. czas oczekiwania |
|---|---|---|
| `Red` | 8.0 | 0 min |
| `Orange` | 4.0 | 10 min |
| `Yellow` | 2.0 | 60 min |
| `Green` | 1.0 | 240 min |
| `Blue` | 0.5 | 480 min |

## 5. Model dziedziny

### Encje (agregaty i byty)

- **`Patient`** (agregat główny) — PESEL, dane osobowe, stan (`PatientState`), przypisanie do
  strefy, rozpoznanie ICD-10, historia Triage (`TriageAssessment`), historia przeniesień
  (`ZoneTransfer`), zlecenia (`MedicalOrder`), blokada edycji.
- **`TriageAssessment`** — ocena segregacji: kategoria, uzasadnienie kliniczne, parametry życiowe,
  czas oceny, autor.
- **`ZoneTransfer`** — wpis przeniesienia pacjenta (skąd → dokąd, powód, autor, czas).
- **`MedicalOrder`** — zlecenie lekarskie (typ, opis, pilność, stan).
- **`User`** — konto, rola, hash i sól hasła, znacznik aktywności.
- **`Zone`** — strefa: kod, nazwa, rodzaj, pojemność, zdolność przyjęciowa.
- **`DutyShift` / `DutyRoster`** — dyżur i grafiku z kontrolą nakładania (BR-17).
- **`StaffZoneAssignment`** — przypisanie personelu do strefy z uzasadnieniem i historią.
- **`AuditLogEntry`** — wpis dziennika audytu.

### Obiekty wartości

`ReassignmentReason`, `Icd10Code`, `ZoneLoadThresholds`, `TriagePolicy`, `LoadRatio`,
`PatientLoadSnapshot`, `ZoneLoadContext`, `PeselNumber` (11 cyfr, cyfra kontrolna wg
rozporządzenia, zakodowana data urodzenia, płeć z dziesiątej cyfry).

### Usługi dziedzinowe

- **`ZoneLoadCalculator`** — wylicza obciążenie strefy (BR-06/BR-06b).
- **`RotationRecommendationEngine`** — dobiera kandydata do rotacji (BR-07).
- **`PatientDischargePolicy`** — weryfikuje warunki wypisu (BR-09/BR-10/BR-11): stan pobytu, rozpoznanie ICD-10, brak otwartych zleceń, oddział przyjmujący oraz uzasadnienie.

### Zdarzenia dziedzinowe

`ZoneOverloadedEvent`, `ZoneLoadNormalizedEvent`, `StaffZoneReassignedEvent`.

## 6. Główne przepływy (przypadki użycia)

1. **Logowanie** → walidacja hasła → odczyt aktywnego dyżuru → ustawienie kontekstu strefy.
2. **Rejestracja pacjenta** → walidacja PESEL → stan `Registered`.
3. **Triage** → nadanie kodu → automatyczny przydział do strefy → stan `Triaged`/`InTreatment`.
4. **Zmiana strefy** → walidacja uprawnień i pojemności → zamknięcie starego przypisania →
   nowe przypisanie → wpis audytowy → odświeżenie sesji.
5. **Monitoring** → wyliczenie obciążenia → zdarzenie przeciążenia → rekomendacja rotacji.
6. **Karta pacjenta** → zlecenia, rozpoznanie, blokada → wypis z SOR (trzy scenariusze) → zapis `PatientDischarge` i wpis audytowy.

## 7. Wymagania niefunkcjonalne

- Trwałość w SQLite (plik lokalny, bez uprawnień administracyjnych).
- Odporność na blokady pliku bazy (polityka ponowień).
- Naturalne brzmienie komunikatów błędów (mapowanie wyjątków domenowych).
- Rozdzielenie logiki biznesowej od interfejsu (MVVM).
