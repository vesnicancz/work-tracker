# WorkTracker

**Aplikace pro sledování pracovní doby s plugin systémem pro integraci s externími systémy.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/architecture-Clean%20Architecture-green)](ARCHITECTURE_REVIEW.md)

---

## O projektu

WorkTracker je desktopová aplikace pro sledování pracovní doby postavená na .NET 10 s Clean Architecture. Nabízí CLI, WPF i Avalonia rozhraní a plugin systém pro integraci s externími systémy (Jira Tempo aj.). Díky Avalonia UI je aplikace dostupná napříč platformami (Windows, Linux, macOS).

**Hlavní funkce:**
- Sledování času na projektech a úkolech (start/stop/edit/delete)
- Automatická detekce Jira ticket ID
- Export worklogs do Jira Tempo
- Detekce překrývajících se časových intervalů
- Denní a týdenní přehledy
- System tray notifikace (WPF, Avalonia)

---

## Rychlý start

### Prerekvizity

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 (pro WPF aplikaci), Linux/macOS (pro Avalonia aplikaci)
- Git

### Instalace

```bash
git clone https://github.com/vesnicancz/work-tracker.git
cd work-tracker

dotnet restore
dotnet build
dotnet test
```

### Spuštění

```bash
# CLI
dotnet run --project src/WorkTracker.CLI

# WPF (Windows only)
dotnet run --project src/WorkTracker.WPF

# Avalonia (cross-platform)
dotnet run --project src/WorkTracker.Avalonia
```

### První použití

```bash
# Začít sledovat práci
dotnet run --project src/WorkTracker.CLI -- start PROJ-123 "Implementace nové funkce"

# Zastavit sledování
dotnet run --project src/WorkTracker.CLI -- stop

# Zobrazit dnešní práci
dotnet run --project src/WorkTracker.CLI -- list

# Odeslat do Tempo
dotnet run --project src/WorkTracker.CLI -- send
```

---

## CLI příkazy

| Příkaz | Popis | Příklad |
|--------|-------|---------|
| `start` | Začít sledovat práci | `worktracker start PROJ-123 "Popis práce" 09:00` |
| `stop` | Zastavit aktivní práci | `worktracker stop 17:30` |
| `status` | Zobrazit aktivní záznam | `worktracker status` |
| `list` | Výpis záznamů (default: dnes) | `worktracker list`, `worktracker list week` |
| `edit` | Upravit záznam | `worktracker edit 5 --ticket=PROJ-456 --end=17:30` |
| `delete` | Smazat záznam | `worktracker delete 5` |
| `send` | Odeslat do Tempo (default: dnes) | `worktracker send`, `worktracker send week` |
| `help` | Nápověda | `worktracker help` |

### Příklady

```bash
# Start s ticket ID, popisem a časem
worktracker start PROJ-123 "Bug fix" 09:00

# Stop se specifickým časem
worktracker stop 17:30

# Editace záznamu
worktracker edit 5 --ticket=PROJ-456 --start=09:00 --end=17:00 --desc="Nový popis"

# Odeslat celý týden do Tempo
worktracker send week
```

**Chování:**
- `start` automaticky zastaví předchozí aktivní záznam
- Časy se zaokrouhlují na minuty
- Validace: alespoň ticket ID nebo popis musí být zadán
- Detekce překrývajících se intervalů

---

## WPF aplikace

Moderní desktopová aplikace s Material Design (Windows only):

- Dashboard s přehledem práce a real-time timer
- Start/Stop tracking s automatickou detekcí Jira kódu
- Editace a mazání záznamů v dialogu
- Odeslání worklogs do Tempo (denní/týdenní)
- System tray ikona s rychlým menu a notifikacemi
- Lokalizace (CZ/EN)

```bash
dotnet run --project src/WorkTracker.WPF
```

---

## Avalonia aplikace

Cross-platform desktopová aplikace (Windows, Linux, macOS) s Fluent theme:

- Stejná funkcionalita jako WPF aplikace
- Přepínatelné Dark/Light motivy (One Dark Pro / One Light palety)
- Material.Icons.Avalonia pro ikony
- CommunityToolkit.Mvvm pro MVVM
- System tray ikona s notifikacemi
- Lokalizace (CZ/EN)

```bash
dotnet run --project src/WorkTracker.Avalonia
```

---

## Konfigurace

### Databáze

Databáze se automaticky vytvoří při prvním spuštění:
```
%LocalAppData%\WorkTracker\worktracker.db
```

Vlastní umístění v `appsettings.json`:
```json
{
  "Database": {
    "Path": "C:\\MojeCesta\\worktracker.db"
  }
}
```

### Tempo/Jira integrace

Přidejte do `appsettings.json`:
```json
{
  "Plugins": {
    "tempo": {
      "TempoBaseUrl": "https://api.tempo.io/core/3",
      "TempoApiToken": "vas-tempo-token",
      "JiraBaseUrl": "https://vase-firma.atlassian.net",
      "JiraEmail": "vas-email@firma.com",
      "JiraApiToken": "vas-jira-token",
      "JiraAccountId": ""
    }
  }
}
```

**Jak získat tokeny:**
1. **Tempo API Token** - Tempo > Settings > API Integration > New Token
2. **Jira API Token** - https://id.atlassian.com/manage-profile/security/api-tokens

> **Bezpečnost:** Necommitujte API tokeny do Gitu! Pro development použijte [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):
> ```bash
> cd src/WorkTracker.CLI
> dotnet user-secrets init
> dotnet user-secrets set "Plugins:tempo:TempoApiToken" "vas-token"
> dotnet user-secrets set "Plugins:tempo:JiraApiToken" "vas-jira-token"
> ```

---

## Plugin systém

WorkTracker podporuje pluginy pro integraci s externími systémy. Pluginy se načítají v izolovaném `AssemblyLoadContext`.

### Vestavěný plugin: Tempo

Automatické nahrávání worklogs do Jira Tempo - překlad issue key na ID, detekce account ID, validace duplicit.

### Vlastní plugin

```csharp
using WorkTracker.Plugin.Abstractions;

public class MujPlugin : WorklogUploadPluginBase
{
    public override PluginMetadata Metadata => new()
    {
        Id = "muj-plugin",
        Name = "Muj Plugin",
        Version = "1.0.0",
        Author = "Autor"
    };

    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog)
    {
        // Upload logika
        return PluginResult<bool>.Success(true);
    }
}
```

Kompletní návod viz [Plugin Development Guide](docs/PLUGIN_DEVELOPMENT.md).

---

## Architektura

Clean Architecture s těmito vrstvami:

```
Presentation (CLI, WPF, Avalonia)
    |
UI.Shared (Models, Service Interfaces, Framework-agnostic Services)
    |
Infrastructure (EF Core, SQLite, Plugins)
    |
Application (Services, Use Cases, Plugin Manager)
    |
Domain (WorkEntry, Business Rules)
```

### Projekty

| Projekt | Popis | Target |
|---------|-------|--------|
| `WorkTracker.Domain` | Business entity a validace | net10.0 |
| `WorkTracker.Application` | Use cases, services, Result pattern | net10.0 |
| `WorkTracker.Infrastructure` | EF Core, SQLite, DI konfigurace | net10.0 |
| `WorkTracker.UI.Shared` | Sdílená UI knihovna (modely, service interfaces, SettingsService, WorklogStateService, LocalizationService) | net10.0 |
| `WorkTracker.CLI` | Konzolové rozhraní (Spectre.Console) | net10.0 |
| `WorkTracker.WPF` | Desktop GUI - Windows (Material Design, MVVM) | net10.0-windows |
| `WorkTracker.Avalonia` | Desktop GUI - cross-platform (Avalonia 11.3, Fluent theme, MVVM) | net10.0 |
| `WorkTracker.Plugin.Abstractions` | Plugin API | net9.0 |
| `WorkTracker.Plugin.Tempo` | Tempo/Jira integrace | net10.0 |

### Struktura repozitáře

```
work-tracker/
├── src/
│   ├── WorkTracker.Domain/              # Business entities
│   ├── WorkTracker.Application/         # Use cases, interfaces
│   ├── WorkTracker.Infrastructure/      # Data access, DI
│   ├── WorkTracker.UI.Shared/            # Shared UI library
│   ├── WorkTracker.CLI/                 # Console app
│   ├── WorkTracker.WPF/                 # Desktop GUI (Windows)
│   ├── WorkTracker.Avalonia/            # Desktop GUI (cross-platform)
│   └── WorkTracker.Plugin.Abstractions/ # Plugin API
├── plugins/
│   └── WorkTracker.Plugin.Tempo/        # Tempo plugin
├── tests/
│   ├── WorkTracker.Domain.Tests/
│   ├── WorkTracker.Application.Tests/
│   └── WorkTracker.Infrastructure.Tests/
└── docs/                                # Dokumentace
```

### Technologie

- **.NET 10.0**, C# 13, nullable reference types
- **Entity Framework Core 10** + SQLite
- **Spectre.Console** (CLI)
- **WPF** + MaterialDesignThemes 5.3 + CommunityToolkit.Mvvm (GUI, Windows)
- **Avalonia 11.3** + Fluent theme + Material.Icons.Avalonia 3.0 + CommunityToolkit.Mvvm (GUI, cross-platform)
- **xUnit** + Moq + FluentAssertions (testy)

### Design Patterns

Repository, Dependency Injection, Result Pattern, Strategy (plugins), MVVM (WPF, Avalonia), Template Method (plugin base), Factory

---

## Pro vývojáře

### Build a testy

```bash
dotnet restore
dotnet build
dotnet test

# S code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Migrace databáze

```bash
dotnet ef migrations add NazevMigrace \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI

dotnet ef database update \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI
```

### Coding Standards

- C# 13, nullable reference types, async/await
- SOLID principy, Clean Code
- Result pattern pro error handling (ne exceptions pro business logiku)
- Structured logging (`ILogger`)
- `.editorconfig` v kořenu repozitáře

---

## Dokumentace

- [Uživatelská příručka](docs/USER_GUIDE.md)
- [Vývojářská příručka](docs/DEVELOPER_GUIDE.md)
- [Plugin Development](docs/PLUGIN_DEVELOPMENT.md)
- [API Reference](docs/API_DOCUMENTATION.md)
- [Architektonické review](ARCHITECTURE_REVIEW.md)

---

## Přispívání

1. Fork repozitáře
2. Vytvořte feature branch (`git checkout -b feature/nova-funkce`)
3. Dodržujte coding standards a přidejte testy
4. Commit (`git commit -m 'feat: popis změny'`)
5. Push a otevřete Pull Request

Při hlášení bugů uveďte: popis problému, kroky k reprodukci, .NET verzi a OS.

---

## Kontakt

- [GitHub Issues](https://github.com/vesnicancz/work-tracker/issues)
- [GitHub Discussions](https://github.com/vesnicancz/work-tracker/discussions)
