# Architektonické Review — WorkTracker

**Datum:** 2026-03-21
**Verze:** na základě commitu `ad4bba1`
**Scope:** Celý codebase (src/, plugins/, tests/)

---

## 1. Přehled architektury

WorkTracker je desktopová aplikace pro sledování pracovní doby s plugin systémem pro odesílání worklogů. Projekt používá **Clean Architecture** se 6 vrstvami:

```
Presentation (CLI, WPF, Avalonia)
    ↓
UI.Shared (sdílené modely, služby, lokalizace)
    ↓
Application (use cases, služby, DTO, pluginy)
    ↓
Infrastructure (EF Core, SQLite, repozitáře, DI)
    ↓
Domain (entity, rozhraní)
    ↓
Plugin.Abstractions (kontrakt pro pluginy)
```

**Tech stack:** .NET 10, C# 13, EF Core 10 + SQLite, CommunityToolkit.Mvvm, MaterialDesignThemes (WPF), Avalonia 11.3, Spectre.Console (CLI)

---

## 2. Co je dobře

| Oblast | Hodnocení |
|--------|-----------|
| **Clean Architecture** | Striktní oddělení vrstev s jasnými závislostmi |
| **Result<T> pattern** | Explicitní error handling bez výjimek pro business logiku |
| **DbContext management** | Správné použití `IDbContextFactory<T>` + `await using` v repository |
| **TimeProvider abstrakce** | Testovatelné zacházení s časem |
| **Plugin system** | `AssemblyLoadContext` pro izolaci, čistý kontrakt přes `IWorklogUploadPlugin` |
| **Async/Await** | Důsledné použití Task-based async vzorů |
| **DI** | Microsoft.Extensions.DependencyInjection konzistentně napříč vrstvami |
| **Lokalizace** | Plná podpora CZ/EN přes resource soubory |
| **Versioning** | MinVer pro sémantické verzování z git tagů |
| **Build konfigurace** | `Directory.Build.props` centralizuje nastavení, `TreatWarningsAsErrors`, nullable enabled |

---

## 3. Nálezy

### 3.1 VYŘEŠENO — LocalizationService převeden na DI

Extrahován `ILocalizationService` interface, `LocalizationService` registrován jako DI singleton v obou App souborech. Všechny ViewModely a služby (8 tříd) injektují `ILocalizationService` místo statického přístupu. XAML markup extensions (`LocalizeExtension`) si zachovávají statický `LocalizationService.Instance`, který se nastavuje z DI přes `SetInstance()`.

---

### 3.2 VYŘEŠENO — PluginManager resource leak opraven

Přidán `try/finally` kolem `context.Unload()` / `_pluginContexts.Remove()` v `PluginManager.UnloadPluginAsync`, takže slovníkový záznam se odstraní i při výjimce.

---

### 3.3 VYŘEŠENO — WPF StackTrace nullable opraven

Přidán `?? "(not available)"` fallback na řádcích 94 a 145 v `App.xaml.cs` pro konzistentní zobrazení chybových hlášek.

---

### 3.4 STŘEDNÍ — Nulové testovací pokrytí UI vrstvy

**Problém:** Existují testy pro Domain (~5), Application (~35) a Infrastructure (~30), ale:

| Netestováno | Důležitost |
|-------------|-----------|
| `WorklogStateService` | Kritická stavová služba — žádné testy pro event raising, stavové přechody |
| `MainViewModel` (obě verze) | Jádro UI logiky — commands, timer, input parsing v kontextu VM |
| `SettingsViewModel` | Ukládání/načítání nastavení |
| `LocalizationService` | Nyní testovatelná díky `ILocalizationService` (viz 3.1) |
| `SettingsService` | Serializace/deserializace JSON |
| `PluginManager.LoadPluginFromFile` | Načítání z externích DLL |

**Doporučení:**

Prioritizovat testy podle rizikovosti:

1. **WorklogStateService** — stavové přechody, event raising, inicializace
2. **MainViewModel** — testovat logiku commands, input parsing, stavové přechody
3. **SettingsService** — serializace/deserializace, chybějící soubor, poškozený JSON
4. **PluginManager.LoadPluginFromFile** — neexistující soubor, nevalidní assembly, chybějící interface


---

## 4. Dependency diagram

```
┌─────────────┐  ┌──────────────────┐  ┌───────────────┐
│  WPF App    │  │  Avalonia App    │  │   CLI App     │
│  (WinExe)   │  │  (WinExe)       │  │   (Exe)       │
└──────┬──────┘  └────────┬─────────┘  └───────┬───────┘
       │                  │                    │
       └──────────┬───────┘                    │
                  ▼                            │
         ┌────────────────┐                    │
         │  UI.Shared     │                    │
         │  (lib)         │                    │
         └───────┬────────┘                    │
                 │                             │
                 ▼                             │
         ┌────────────────┐                    │
         │  Application   │◄───────────────────┘
         │  (lib)         │
         └──┬──────────┬──┘
            │          │
            ▼          ▼
   ┌────────────┐  ┌──────────────────────┐
   │  Domain    │  │ Plugin.Abstractions  │
   │  (lib)     │  │ (lib)                │
   └────────────┘  └──────────┬───────────┘
                              │
                              ▼
                   ┌──────────────────┐
                   │  Plugin.Tempo    │
                   │  (lib)           │
                   └──────────────────┘

   ┌────────────────────────────────────┐
   │  Infrastructure (lib)              │
   │  References: Domain, Application,  │
   │  Plugin.Tempo (embedded plugin)    │
   └────────────────────────────────────┘
```

---

## 5. Metriky

| Metrika | Hodnota |
|---------|---------|
| Počet projektů | 12 (8 src + 3 tests + 1 plugin) |
| Target framework | .NET 10.0 |
| Testů celkem | ~198 |
| Pokrytí UI vrstvy | 0 % |
| NuGet závislostí | ~20 unikátních |

---

## 6. Shrnutí

### Vyřešeno (3.1, 3.2, 3.3)

| Nález | Řešení |
|-------|--------|
| LocalizationService mimo DI | Extrahován `ILocalizationService`, registrován v DI, injektován do ViewModelů a služeb |
| PluginManager resource leak | Přidán `try/finally` kolem `context.Unload()` |
| WPF StackTrace nullable | Přidán `?? "(not available)"` fallback |

### Zamítnuto

| Nález | Důvod |
|-------|-------|
| Infrastructure → Plugin.Tempo | Embedded plugin je de facto součást aplikace, decentralizace by byla horší |
| Duplikace ViewModelů WPF/Avalonia | ViewModely jsou záměrně platformně specifické |
| WorklogStateService bez synchronizace | Background tasky nedávají smysl, vše běží na UI threadu |
| WPF polling loop na `_host` | Řeší reálný problém — `NullReferenceException` při autostartu s Windows |
| AnalysisMode zakomentovaný | Úmyslně |

### Otevřeno

| # | Nález | Závažnost | Effort |
|---|-------|-----------|--------|
| 3.4 | Nulové testy UI vrstvy | Střední | Vysoký |
