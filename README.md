# WorkTracker

**Moderní aplikace pro sledování pracovní doby s podporou pluginů pro integraci s externími systémy**

[![.NET Version](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Architecture](https://img.shields.io/badge/architecture-Clean%20Architecture-green)](ARCHITECTURE_REVIEW.md)

---

## 📋 Obsah

- [O Projektu](#-o-projektu)
- [Funkce](#-funkce)
- [Rychlý Start](#-rychlý-start)
- [Instalace](#-instalace)
- [Použití](#-použití)
- [Plugin Systém](#-plugin-systém)
- [Konfigurace](#-konfigurace)
- [Pro Vývojáře](#-pro-vývojáře)
- [Dokumentace](#-dokumentace)
- [Přispívání](#-přispívání)
- [Licence](#-licence)

---

## 🎯 O Projektu

WorkTracker je **profesionální aplikace pro sledování pracovní doby** postavená na .NET 9.0 s důrazem na:
- **Čistou architekturu** (Clean Architecture)
- **Extensibilitu** prostřednictvím plugin systému
- **Flexibilitu** - CLI i GUI rozhraní
- **Integraci** s externími systémy (Jira Tempo, atd.)

### Použití

- ✅ Sledování času stráveného na projektech a úkolech
- ✅ Export pracovních záznamů do Jira Tempo
- ✅ Automatická detekce Jira ticket ID
- ✅ Správa pracovních záznamů (start, stop, edit, delete)
- ✅ Přehled denních a týdenních aktivit
- ✅ Integrace s dalšími systémy prostřednictvím pluginů

---

## ✨ Funkce

### Core Features

- **⏱️ Time Tracking**
  - Start/stop sledování pracovní doby
  - Automatické ukončení předchozí aktivní práce
  - Detekce překrývajících se časových intervalů
  - Podpora pro Jira ticket ID a popis práce

- **🔌 Plugin Systém**
  - Extensibilní architektura pro integraci s externími systémy
  - Izolované načítání pluginů (AssemblyLoadContext)
  - Konfigurovatelné pluginy přes appsettings.json
  - Out-of-the-box Tempo plugin pro Jira

- **💻 Dual UI**
  - **CLI** - Rychlé příkazové rozhraní s Spectre.Console
  - **WPF** - Moderní desktop GUI s Material Design

- **📊 Reporting**
  - Denní a týdenní přehledy
  - Export do externích systémů
  - Náhled před odesláním

- **🛡️ Data Integrity**
  - SQLite databáze
  - Entity Framework Core
  - Automatické migrace
  - Validace časových intervalů

---

## 🚀 Rychlý Start

### Prerekvizity

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows 10/11 (pro WPF aplikaci)
- Git

### Instalace v 3 krocích

```bash
# 1. Klonování repository
git clone https://github.com/yourusername/WorkTracker.git
cd WorkTracker

# 2. Build projektu
dotnet build

# 3. Spuštění CLI
dotnet run --project src/WorkTracker.CLI

# NEBO spuštění WPF
dotnet run --project src/WorkTracker.WPF
```

### První použití

```bash
# Začít sledovat práci
dotnet run --project src/WorkTracker.CLI -- start PROJ-123 "Implementace nové funkce"

# Zastavit sledování
dotnet run --project src/WorkTracker.CLI -- stop

# Zobrazit dnešní práci
dotnet run --project src/WorkTracker.CLI -- list today

# Odeslat do Tempo
dotnet run --project src/WorkTracker.CLI -- send today
```

---

## 📦 Instalace

### Development Setup

1. **Klonování repository**
   ```bash
   git clone https://github.com/yourusername/WorkTracker.git
   cd WorkTracker
   ```

2. **Restore závislostí**
   ```bash
   dotnet restore
   ```

3. **Build solution**
   ```bash
   dotnet build
   ```

4. **Spuštění testů**
   ```bash
   dotnet test
   ```

### Konfigurace Databáze

Databáze se automaticky vytvoří při prvním spuštění v:
```
%LocalAppData%\WorkTracker\worktracker.db
```

Pro custom umístění upravte `appsettings.json`:
```json
{
  "Database": {
    "Path": "C:\\CustomPath\\worktracker.db"
  }
}
```

---

## 🎮 Použití

### CLI Rozhraní

#### Start Tracking

```bash
# Start s Jira ticket ID
worktracker start PROJ-123

# Start s popisem
worktracker start "Implementace API"

# Start s ticket ID a popisem
worktracker start PROJ-123 "Oprava bugu v autentizaci"

# Start se specifickým časem
worktracker start PROJ-123 --time 09:00

# Vytvoření dokončené práce (s end time)
worktracker start PROJ-123 --start 09:00 --end 10:30
```

#### Stop Tracking

```bash
# Zastavit aktuální práci
worktracker stop

# Zastavit se specifickým časem
worktracker stop --time 17:00
```

#### List Entries

```bash
# Dnešní záznamy
worktracker list today

# Včerejší záznamy
worktracker list yesterday

# Specifický datum
worktracker list 2025-11-01

# Tento týden
worktracker list week

# Minulý týden
worktracker list lastweek
```

#### Edit Entry

```bash
# Editovat záznam
worktracker edit 5 --ticket PROJ-456 --start 09:00 --end 17:00

# Změnit jen popis
worktracker edit 5 --description "Nový popis práce"

# Změnit jen čas
worktracker edit 5 --start 08:30 --end 16:30
```

#### Delete Entry

```bash
# Smazat záznam
worktracker delete 5
```

#### Submit to Tempo

```bash
# Odeslat dnešní práci
worktracker send today

# Odeslat včerejší práci
worktracker send yesterday

# Odeslat specifický datum
worktracker send 2025-11-01

# Odeslat celý týden
worktracker send week

# Náhled před odesláním (dry-run)
worktracker send today --preview
```

#### Status

```bash
# Zobrazit aktuální aktivní práci
worktracker status
```

### WPF Aplikace

1. **Spuštění**
   ```bash
   dotnet run --project src/WorkTracker.WPF
   ```

2. **Funkce GUI**
   - 🎯 Dashboard s přehledem práce
   - ▶️ Start/Stop tracking tlačítka
   - 📝 Editace záznamů v dialogu
   - 📤 Submit do Tempo
   - ⚙️ Nastavení
   - 🔔 System tray ikona s notifikacemi

3. **System Tray**
   - Aplikace běží na pozadí
   - Rychlý start/stop z tray menu
   - Notifikace při startu/stopu práce

---

## 🔌 Plugin Systém

WorkTracker podporuje extensibilní plugin systém pro integraci s externími službami.

### Dostupné Pluginy

#### Tempo Plugin (Built-in)

Integrace s Jira Tempo pro automatické nahrávání worklogs.

**Konfigurace** (`appsettings.json`):
```json
{
  "Plugins": {
    "tempo": {
      "TempoBaseUrl": "https://api.tempo.io/core/3",
      "TempoApiToken": "your-tempo-token",
      "JiraBaseUrl": "https://your-company.atlassian.net",
      "JiraEmail": "your-email@company.com",
      "JiraApiToken": "your-jira-token",
      "JiraAccountId": "optional-account-id"
    }
  }
}
```

**Získání API Tokenů:**

1. **Tempo API Token**
   - Přihlaste se do Tempo
   - Settings → API Integration
   - Generate New Token

2. **Jira API Token**
   - https://id.atlassian.com/manage/api-tokens
   - Create API token

### Vývoj Vlastního Pluginu

Viz [PLUGIN_DEVELOPMENT.md](docs/PLUGIN_DEVELOPMENT.md) pro kompletní guide.

**Quick Example:**

```csharp
using WorkTracker.Plugin.Abstractions;

public class MyCustomPlugin : WorklogUploadPluginBase
{
    public override PluginMetadata Metadata => new()
    {
        Id = "my-custom",
        Name = "My Custom Plugin",
        Version = "1.0.0",
        Author = "Your Name"
    };

    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog)
    {
        // Your upload logic here
        return PluginResult<bool>.Success(true);
    }
}
```

---

## ⚙️ Konfigurace

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "WorkTracker": "Debug"
    }
  },
  "Database": {
    "Path": "%LocalAppData%\\WorkTracker\\worktracker.db"
  },
  "Plugins": {
    "tempo": {
      "TempoBaseUrl": "https://api.tempo.io/core/3",
      "TempoApiToken": "",
      "JiraBaseUrl": "",
      "JiraEmail": "",
      "JiraApiToken": "",
      "JiraAccountId": ""
    }
  }
}
```

### Environment Variables

```bash
# Override database path
export WORKTRACKER_DB_PATH="C:\Custom\Path\worktracker.db"

# Override Tempo token
export WORKTRACKER_TEMPO_TOKEN="your-token"
```

### User Secrets (Doporučeno pro development)

```bash
# Inicializace user secrets
cd src/WorkTracker.CLI
dotnet user-secrets init

# Nastavení secrets
dotnet user-secrets set "Plugins:tempo:TempoApiToken" "your-token"
dotnet user-secrets set "Plugins:tempo:JiraApiToken" "your-jira-token"
```

---

## 👨‍💻 Pro Vývojáře

### Architektura

WorkTracker implementuje **Clean Architecture** s těmito vrstvami:

```
┌─────────────────────────────────────┐
│   Presentation (CLI, WPF)           │
├─────────────────────────────────────┤
│   Infrastructure                    │
├─────────────────────────────────────┤
│   Application                       │
├─────────────────────────────────────┤
│   Domain                            │
└─────────────────────────────────────┘
```

**Projekty:**
- `WorkTracker.Domain` - Business entities a logika
- `WorkTracker.Application` - Use cases a orchestrace
- `WorkTracker.Infrastructure` - Data access, EF Core
- `WorkTracker.CLI` - Command-line interface
- `WorkTracker.WPF` - Desktop GUI
- `WorkTracker.Plugin.Abstractions` - Plugin API
- `WorkTracker.Plugin.Tempo` - Tempo integrace

Viz [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md) pro detailní analýzu.

### Development Setup

```bash
# Clone repository
git clone https://github.com/yourusername/WorkTracker.git
cd WorkTracker

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Run CLI in development
dotnet run --project src/WorkTracker.CLI

# Run WPF in development
dotnet run --project src/WorkTracker.WPF
```

### Testing

```bash
# Spustit všechny testy
dotnet test

# Spustit s code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Spustit specifický test project
dotnet test tests/WorkTracker.Application.Tests
```

### Coding Standards

- ✅ C# 12 s nullable reference types
- ✅ Async/await pro IO operace
- ✅ SOLID principles
- ✅ Clean Code practices
- ✅ XML documentation pro public API
- ✅ Unit tests pro business logic

### Design Patterns

- **Repository Pattern** - Data access abstrakce
- **Dependency Injection** - Loose coupling
- **Result Pattern** - Functional error handling
- **Strategy Pattern** - Plugin system
- **MVVM** - WPF presentation layer
- **Template Method** - Plugin base classes
- **Factory Pattern** - Entity creation

### Code Structure

```
WorkTracker/
├── src/
│   ├── WorkTracker.Domain/          # Business entities
│   ├── WorkTracker.Application/     # Use cases
│   ├── WorkTracker.Infrastructure/  # Data access
│   ├── WorkTracker.CLI/            # Console app
│   ├── WorkTracker.WPF/            # Desktop GUI
│   └── WorkTracker.Plugin.*/       # Plugin projects
├── tests/
│   ├── WorkTracker.Domain.Tests/
│   ├── WorkTracker.Application.Tests/
│   └── WorkTracker.Infrastructure.Tests/
├── plugins/                         # External plugins
└── docs/                           # Documentation
```

---

## 📚 Dokumentace

### Pro Uživatele

- [USER_GUIDE.md](docs/USER_GUIDE.md) - Kompletní uživatelská příručka
- [FAQ.md](docs/FAQ.md) - Často kladené otázky

### Pro Vývojáře

- [DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md) - Vývojářská dokumentace
- [PLUGIN_DEVELOPMENT.md](docs/PLUGIN_DEVELOPMENT.md) - Tvorba pluginů
- [API_DOCUMENTATION.md](docs/API_DOCUMENTATION.md) - API reference
- [ARCHITECTURE_REVIEW.md](ARCHITECTURE_REVIEW.md) - Architektonická analýza

### Changelog

- [CHANGELOG.md](CHANGELOG.md) - Historie změn

---

## 🤝 Přispívání

Příspěvky jsou vítány! Před začátkem práce prosím:

1. **Fork repository**
2. **Vytvořte feature branch** (`git checkout -b feature/amazing-feature`)
3. **Dodržujte coding standards** (viz [DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md))
4. **Přidejte testy** pro novou funkcionalitu
5. **Commitněte změny** (`git commit -m 'Add amazing feature'`)
6. **Push do branch** (`git push origin feature/amazing-feature`)
7. **Otevřete Pull Request**

### Contribution Guidelines

- ✅ Dodržujte Clean Architecture principy
- ✅ Pište unit testy (minimum 80% coverage)
- ✅ Dokumentujte public API (XML comments)
- ✅ Používejte async/await pro IO operace
- ✅ Validujte vstupy
- ✅ Logujte důležité operace
- ✅ Handlete chyby pomocí Result pattern

### Reporting Issues

Při hlášení bugu prosím uveďte:
- 🐛 Popis problému
- 📋 Kroky k reprodukci
- 💻 Environment (.NET verze, OS, atd.)
- 📸 Screenshots (pokud je to relevantní)
- 📝 Log výstupy

---

## 🔐 Bezpečnost

### Security Best Practices

- **🔒 Credentials Management**
  - NIKDY necommitujte API tokeny do Git
  - Používejte User Secrets pro development
  - Používejte Environment Variables nebo Credential Manager pro production

- **🛡️ Data Protection**
  - Databáze je uložena lokálně
  - Žádné citlivé údaje nejsou odesílány třetím stranám (kromě nakonfigurovaných pluginů)

### Reporting Security Issues

Pokud objevíte security vulnerability, prosím **NEOTEVÍREJTE public issue**.
Namísto toho:
- 📧 Napište na: security@yourproject.com
- 🔐 Použijte GPG key pro šifrování (pokud je dostupný)

---

## 🏆 Roadmap

### Verze 1.1 (Plánováno)

- [ ] Multi-user support
- [ ] Cloud synchronizace
- [ ] Mobile app (MAUI)
- [ ] Advanced reporting
- [ ] Export do Excel/CSV
- [ ] Integrace s dalšími systémy (GitHub, GitLab, Azure DevOps)

### Verze 1.2 (Budoucnost)

- [ ] Machine learning pro automatickou kategorizaci
- [ ] Team collaboration features
- [ ] API pro third-party integrace
- [ ] Web dashboard
- [ ] Offline mode s sync

---

## 📄 Licence

Tento projekt je licencován pod MIT License - viz [LICENSE](LICENSE) soubor pro detaily.

```
MIT License

Copyright (c) 2025 WorkTracker Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software...
```

---

## 🙏 Poděkování

- [.NET Team](https://github.com/dotnet) - za skvělý framework
- [Entity Framework Core](https://github.com/dotnet/efcore) - za ORM
- [Spectre.Console](https://github.com/spectreconsole/spectre.console) - za krásné CLI
- [Material Design In XAML](http://materialdesigninxaml.net/) - za WPF theming
- [Tempo.io](https://tempo.io) - za API dokumentaci

---

## 📞 Kontakt

- 🐛 **Issues**: [GitHub Issues](https://github.com/yourusername/WorkTracker/issues)
- 💬 **Diskuze**: [GitHub Discussions](https://github.com/yourusername/WorkTracker/discussions)
- 📧 **Email**: support@yourproject.com
- 🌐 **Website**: https://worktracker.example.com

---

## ⭐ Star History

Pokud se vám projekt líbí, prosím dejte mu hvězdičku! ⭐

[![Star History Chart](https://api.star-history.com/svg?repos=yourusername/WorkTracker&type=Date)](https://star-history.com/#yourusername/WorkTracker&Date)

---

**Made with ❤️ by WorkTracker Contributors**
