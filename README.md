# PwSWproject
Gaz3ll&Rosemary

## Dokumentacja

| Etap | Zakres | Plik |
|---|---|---|
| I | Analiza wymagań i model dziedzinowy | [`docs/ETAP-I-Analiza.md`](docs/ETAP-I-Analiza.md) |
| II | Projekt architektury i model techniczny | [`docs/ETAP-II-Projekt.md`](docs/ETAP-II-Projekt.md) |
| III | Implementacja, testy i uruchomienie | [`docs/ETAP-III-Implementacja.md`](docs/ETAP-III-Implementacja.md) |

## Szybki start

```powershell
dotnet build SOR.MedicalEmergencySystem.slnx    # kompilacja
dotnet test SOR.MedicalEmergencySystem.slnx     # 111 testów
dotnet run --project src/SOR.Presentation/SOR.Presentation.csproj
```

Konta demonstracyjne, opis ekranu po zalogowaniu (sekcje `PRZYJĘCIA` / `PORADNIA` / `INNE`),
moduł karty pobytu z raportem z dyżuru i badaniami obrazowym oraz obsługa motywu ciemnego
są opisane w `docs/ETAP-III-Implementacja.md`.