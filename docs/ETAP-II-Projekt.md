# ETAP II — Projekt architektury i model techniczny

## 1. Architektura warstwowa

System zbudowano w architekturze **Clean/Onion** z twardym kierunkiem zależności do środka.
Każda warstwa to osobny projekt (`src/`), a zależności są jednokierunkowe:

```
SOR.Presentation ──► SOR.Infrastructure ──► SOR.Application ──► SOR.Domain
        │                                        ▲                     ▲
        └────────────────────────────────────────┴─────────────────────┘
```

| Projekt | Odpowiedzialność |
|---|---|
| `SOR.Domain` | Encje, obiekty wartości, reguły biznesowe, usługi dziedzinowe, zdarzenia. Brak zależności zewnętrznych. |
| `SOR.Application` | Kontrakty (interfejsy), DTO, logika przypadków użycia (serwisy aplikacyjne), mapowanie. |
| `SOR.Infrastructure` | EF Core + SQLite, repozytoria, Unit of Work, inicjalizacja i dane startowe, Composition Root. |
| `SOR.Presentation` | Aplikacja WPF w wzorcu MVVM, widoki, ViewModele, komendy. |
| `SOR.Domain.Tests` | Testy jednostkowe i integracyjne (xUnit). |

Reguła zależności: `Domain` nie zna nikogo; `Application` zna `Domain`; `Infrastructure` zna
`Application` i `Domain`; `Presentation` zna `Application`, `Infrastructure` (tylko do rejestracji
DI) i `Domain`.

## 2. Wzorce projektowe

| Wzorzec | Zastosowanie |
|---|---|
| **Repository** | `IPatientRepository`, `IZoneRepository`, `IAuditLogRepository`, `ITriageAssessmentRepository`, `IZoneTransferRepository`, … |
| **Unit of Work** | `IUnitOfWork` / `SqliteUnitOfWork` — transakcja obejmująca operację biznesową i wpis audytowy (BR-25). |
| **Value Object** | `ReassignmentReason`, `Icd10Code`, `ZoneLoadThresholds`, `TriagePolicy`, `LoadRatio`. |
| **Strategy** | `IZoneLoadCalculator`, `IRotationRecommendationEngine` — wymienne algorytmy domenowe. |
| **Domain Events** | `IDomainEventPublisher`, zdarzenia `ZoneOverloadedEvent`, `StaffZoneReassignedEvent`. |
| **MVVM** | `ObservableObject`, `RelayCommand` / `AsyncRelayCommand`, ViewModele, wiązania danych. |
| **Composition Root** | `ServiceRegistration.AddSorSystem`, `PresentationServiceRegistration.AddPresentation`. |
| **Factory Method** | Statyczne metody tworzące encje (`Patient.Register`, `Zone.Create`, `MedicalOrder.Create`). |

## 3. Model danych (SQLite / EF Core)

Schemat tworzony przez `EnsureCreatedAsync` (bez migracji), konfiguracja przez klasy
`IEntityTypeConfiguration` w `SorDbContext`.

| Tabela | Kluczowe kolumny |
|---|---|
| `Users` | `Id`, `Login`, `DisplayName`, `Role`, `PasswordHash`, `PasswordSalt`, `IsActive` |
| `Zones` | `Id`, `Code`, `Name`, `Kind`, `Capacity` |
| `Patients` | `Id`, `Pesel`, `FirstName`, `LastName`, `DateOfBirthUtc`, `Gender`, `State`, `ZoneId`, `Diagnosis`, `RowVersion` |
| `TriageAssessments` | `Id`, `PatientId`, `Category`, `ClinicalJustification`, `VitalSignsSummary`, `AssessedAtUtc`, `AssessedByUserId`, `RowVersion` |
| `ZoneTransfers` | `Id`, `PatientId`, `FromZoneId`, `ToZoneId`, `InitiatedByUserId`, `Reason`, `TransferredAtUtc` |
| `MedicalOrders` | `Id`, `PatientId`, `Type`, `Description`, `State`, `IsUrgent`, `OrderedAtUtc` |
| `StaffZoneAssignments` | `Id`, `UserId`, `ZoneId`, `Kind`, `EffectiveFromUtc`, `EffectiveToUtc`, `SupersedesAssignmentId` |
| `DutyShifts` | `Id`, `UserId`, `ZoneId`, `StartsAtUtc`, `EndsAtUtc`, `IsCancelled` |
| `AuditLogEntries` | `Id`, `ActionType`, `UserId`, `ActorLogin`, `OccurredAtUtc`, `Details`, `Success` |

### Rozwiązania specyficzne dla SQLite

- **`DateTimeOffset` → `long` (binary)**: globalny konwerter `DateTimeOffsetToBinaryConverter`
  w `SorDbContext.ConfigureConventions`, ponieważ SQLite nie porównuje natywnie `DateTimeOffset`.
- **Współbieżność optymistyczna (BR-20)**: `RowVersion` nie jest generowany przez magazyn
  (`IsConcurrencyToken().ValueGeneratedNever()`), a token nadaje aplikacja w
  `StampConcurrencyTokens` przed `SaveChangesAsync`.
- **Jawne dodawanie encji potomnych**: EF Core traktuje encję odnalezioną w nawigacji z ustawionym
  kluczem obcym jako *istniejącą*. `TriageAssessment` i `ZoneTransfer` są więc jawnie dodawane
  przez repozytoria (`ITriageAssessmentRepository`, `IZoneTransferRepository`), co wymusza `INSERT`.

## 4. Wstrzykiwanie zależności (DI)

Composition Root rejestruje wszystkie warstwy:

```
IClock, IIdGenerator, ZoneLoadThresholds, IZoneLoadCalculator,
IRotationRecommendationEngine            → Singleton
SorDbContext, IUnitOfWork                → Scoped
IDomainEventPublisher, IAuditLogService,
IZoneLoadQueryService, IStaffRotationService,
IAuthenticationService, IPatientService,
IZoneLoadMonitoringService               → Scoped
UiThreadDispatcher                       → Singleton
SessionViewModel, PatientBoardViewModel,
MainViewModel                            → Scoped
```

### Rozwiązanie cyklu zależności

Pierwotnie `ZoneLoadMonitoringService` i `StaffRotationService` zależały od siebie nawzajem.
Wydzielono bezskutkowy odczyt obciążenia (`IZoneLoadQueryService`) od obserwatora publikującego
zdarzenia (`IZoneLoadMonitoringService`). Dzięki temu graf jest **acykliczny**:

```
IZoneLoadQueryService ─► IZoneLoadCalculator, IRotationRecommendationEngine
IZoneLoadMonitoringService ─► IZoneLoadQueryService, IAuditLogService, IDomainEventPublisher
IStaffRotationService ─► IZoneLoadQueryService, IAuthenticationService, IDomainEventPublisher
```

### Cykl życia w aplikacji desktopowej

WPF nie ma zakresu żądania, dlatego `App.OnStartup` tworzy **jeden zakres na cały proces**
(`CreateAsyncScope`). Dzięki temu sesja użytkownika i kontekst EF Core są współdzielone —
zgodnie z ideą logowania kontekstowego (jedna strefa obowiązuje przez całą sesję).

## 5. Bezpieczeństwo

- **Hasła**: PBKDF2-HMAC-SHA256, 100 000 iteracji, 16-bajtowa sól (`PasswordHasher`).
- **Blokada konta**: 3 nieudane próby → 60 s blokady (stan w pamięci procesu).
- **Brak wycieku informacji**: dla nieistniejącego loginu wykonywany jest kosztowny „dummy hash”,
  aby czas odpowiedzi był stały.
- **Autoryzacja kontekstowa**: BR-05/BR-07/BR-08 w `StaffRotationService`, BR-12/BR-13 w `PatientService`.
- **Audyt**: każda operacja krytyczna zapisywana w `AuditLogEntries` w tej samej transakcji.

## 6. Obsługa błędów

Warstwa prezentacji zna wyłącznie bazowe typy wyjątków i tłumaczy je na komunikaty użytkownika
(`ExceptionMessageMapper` → `UserFacingError`). Wyjątki domenowe (`DomainException`,
`ValidationException`) i aplikacyjne (`SorApplicationException`, `AuthorizationException`,
`ZoneCapacityExceededException`, `PersistenceException`) nie wyciekają w postaci surowej do UI.

## 7. Odporność (Resilience)

`SqliteRetryPolicy` ponawia operacje przy błędach `SQLITE_BUSY`/`SQLITE_LOCKED` z wykładniczym
backoffem. Używany przez `SqliteUnitOfWork` przy każdej transakcji, co chroni przed chwilowymi
blokadami pliku bazy.
