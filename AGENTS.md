<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

### Gdy brak CodeGraph — instalacja

Treść między znacznikami `CODEGRAPH_START`/`CODEGRAPH_END` jest generowana przez CodeGraph i jest
nadpisywana przy każdym `codegraph install`/`upgrade` — nie edytuj jej ręcznie. Poniższe kroki
wykonaj tylko wtedy, gdy w repo nie ma katalogu `.codegraph/`. Każdą z tych komend wykonaj osobno.:

```powershell
npx @colbymchenry/codegraph                                  # interaktywny kreator (opcjonalny)
codegraph install --target=opencode --location=local --yes   # wpina serwer MCP do opencode (bez --yes pyta interaktywnie)
codegraph init -y                                            # buduje indeks .codegraph/
```

> **Ważne:** po instalacji MCP plik `opencode.json` ma **NIE ZMIENIĆ SIĘ** — konfiguracja
> pozostaje dokładnie taka, jaka była przed instalacją. MCP ma **od razu uruchomić się**,
> bez dodatkowej edycji `opencode.json`.

opencode.json
```
{
  "$schema": "https://opencode.ai/config.json",
  "instructions": [
    "AGENTS.md"
  ],
  "mcp": {
    "servers": {
      "enabled": true,
      "codegraph": {
        "type": "local",
        "command": [
          "codegraph",
          "serve",
          "--mcp"
        ]
      }
    }
  }
}
```
# AGENTS.md

Polish-language **.NET 9 / WPF** desktop app for a hospital Emergency Department (SOR).
4-layer clean architecture over EF Core + SQLite. All code comments, XML docs, UI strings,
docs and commit messages are in **Polish** — keep it that way.

## Commands

```powershell
dotnet build SOR.MedicalEmergencySystem.slnx      # .slnx (new format), NOT .sln
dotnet test  SOR.MedicalEmergencySystem.slnx      # 111 xUnit tests
dotnet run --project src/SOR.Presentation/SOR.Presentation.csproj

# run one test class (filter is on the type name, substring match):
dotnet test tests/SOR.Domain.Tests/SOR.Domain.Tests.csproj --filter "FullyQualifiedName~PeselNumberTests"
```

- Requires **Windows** (WPF) and **.NET SDK 10** (projects target `net9.0` / `net9.0-windows`).
  Building the full solution needs Windows; the test project references only Domain/Application/Infrastructure.
- No CI, no linter/formatter config — verification is `dotnet build` + `dotnet test`.

## Layout (dependency direction points inward)

- `src/SOR.Domain` — entities, value objects, domain services, events. **Zero dependencies.**
- `src/SOR.Application` — interfaces, DTOs, application services, mapping. Depends on Domain only.
- `src/SOR.Infrastructure` — EF Core + SQLite, repositories, UnitOfWork, seeder, DI. Depends on Application.
- `src/SOR.Presentation` — WPF MVVM. Depends on all layers.
- `tests/SOR.Domain.Tests` — xUnit; despite the name it also exercises Application + Infrastructure.

## DI / composition

- `SOR.Infrastructure/ServiceRegistration.AddSorSystem(...)` is the **only** place concrete
  implementations are registered; every other layer works against interfaces.
- `App.xaml.cs` is the composition root: builds the container, initializes the DB, then creates
  **one root DI scope for the entire process** — the EF `DbContext` and user session are shared by
  all ViewModels. Don't assume per-request scoping.
- Application services and ViewModels are `Scoped`; clock, id generator, load thresholds and theme are `Singleton`.

## Persistence

- SQLite file at `%LOCALAPPDATA%\SOR\sor.db`, created on startup. **No EF migrations** — schema uses `EnsureCreated`.
- `DatabaseInitializer` compares model tables against the file; if the model adds a table it **drops and
  recreates the whole schema** and reseeds. Model changes silently wipe local data — expected behavior.
- `DatabaseSeeder` is idempotent and re-runs on startup to fill missing demo data.

## Conventions / gotchas

- Culture is forced to `pl-PL` with `dd.MM.yyyy` at startup — do not rely on machine locale.
- WPF theming: styles may use **only** `DynamicResource` theme brushes (`AppBackgroundBrush`,
  `SurfaceBrush`, `TextPrimaryBrush`, `AccentBrush`, …). A hard-coded color will not repaint on
  Light/Dark switch. Theme choice persists in `%LOCALAPPDATA%\SOR\ui-theme.json`.
- Hand-rolled MVVM (`Mvvm/ObservableObject.cs`, `Mvvm/RelayCommand.cs`) — no MVVM toolkit;
  icons are vector `Path`, no icon libraries.
- Bindings to read-only properties (`ProgressBar.Value`, DataGrid columns, `TabControl.SelectedIndex`)
  must be `Mode=OneWay` to avoid the WPF "TwoWay binding on read-only property" crash.
- `SOR.Domain` grants `InternalsVisibleTo` to `SOR.Infrastructure` and `SOR.Domain.Tests`.

## Demo accounts (for manual runs)

| Login | Password | Role / zone |
|---|---|---|
| `ordynator` | `SOR2026!ord` | Coordinator (all zones) |
| `lekarz.trm` | `SOR2026!trm` | Doctor, TRM |
| `ratownik.trm` | `SOR2026!rtrm` | Paramedic, TRM |

Full account/zone matrix and the paramedic permission rules (BR-13) are in `docs/ETAP-III-Implementacja.md`.

## Docs

`docs/ETAP-I-Analiza.md`, `ETAP-II-Projekt.md`, `ETAP-III-Implementacja.md` are the authoritative
(Polish) spec: requirements, architecture, and run/UI/permission details. Consult ETAP-III before
UI or domain changes; it also lists known limitations.
