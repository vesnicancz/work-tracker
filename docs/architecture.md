# Architektura

WorkTracker je postavený na **Clean Architecture** se čtyřmi vrstvami a plugin systémem. Tento dokument popisuje, jak jsou vrstvy uspořádané, jak komunikují a jaké vzory projekt používá.

Pokud tě zajímá **jak napsat vlastní plugin**, jdi rovnou na [plugin-development.md](plugin-development.md). Pokud hledáš **setup, build a testy**, viz [developer-guide.md](developer-guide.md).

---

## Obsah

1. [Diagram vrstev](#diagram-vrstev)
2. [Vrstvy a odpovědnosti](#vrstvy-a-odpovědnosti)
3. [Dependency flow](#dependency-flow)
4. [Presentation: CLI, WPF, Avalonia](#presentation-cli-wpf-avalonia)
5. [Plugin systém](#plugin-systém)
6. [Klíčové vzory](#klíčové-vzory)
7. [Datový tok: příklad odeslání worklogu](#datový-tok-příklad-odeslání-worklogu)

---

## Diagram vrstev

```
┌─────────────────────────────────────────────────────────────────────┐
│  Presentation                                                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐   │
│  │ WorkTracker  │  │ WorkTracker  │  │ WorkTracker.Avalonia     │   │
│  │ .CLI         │  │ .WPF         │  │ (cross-platform desktop) │   │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────────┘   │
│         │                 │                     │                   │
│         │         ┌───────┴─────────────────────┘                   │
│         │         │                                                 │
│         │  ┌──────▼───────────────────────────────────────────┐     │
│         │  │ WorkTracker.UI.Shared                            │     │
│         │  │ - ViewModely (CommunityToolkit.Mvvm)             │     │
│         │  │ - Orchestrátory (koordinují služby mezi UI)      │     │
│         │  │ - ISettingsService, ILocalizationService         │     │
│         │  │ - IPomodoroService                               │     │
│         │  └──────────────────┬───────────────────────────────┘     │
└─────────────────────────────┬─┼─────────────────────────────────────┘
                              │ │
┌─────────────────────────────▼─▼─────────────────────────────────────┐
│  Application                                                        │
│  - IWorkEntryService  (use cases nad WorkEntry)                     │
│  - IWorklogSubmissionService  (pipeline odesílání worklogů)         │
│  - IUnitOfWork / IUnitOfWorkFactory  (transakce)                    │
│  - ISecureStorage  (abstrakce nad OS credential storem)             │
│  - IPluginManager  (registr a lifecycle pluginů)                    │
│  - Result<T>, DTO, validátory, mapery                               │
└─────────────────────────────┬───────────────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────────────┐
│  Domain                                                             │
│  - WorkEntry (entity)                                               │
│  - IWorkEntryRepository (interface)                                 │
│  - Business pravidla (validace časů, kategorizace, překryvy)        │
│  - Žádné závislosti navenek                                         │
└─────────────────────────────┬───────────────────────────────────────┘
                              │ implementováno v
┌─────────────────────────────▼───────────────────────────────────────┐
│  Infrastructure                                                     │
│  - WorkTrackerDbContext (EF Core + SQLite)                          │
│  - WorkEntryRepository, UnitOfWork, UnitOfWorkFactory               │
│  - CredentialStoreSecureStorage (GitCredentialManager)              │
│  - PluginManager, PluginLoader, PluginLoadContext (ALC)             │
│  - MsalTokenProvider, MsalTokenProviderFactory                      │
│  - DependencyInjection.AddInfrastructure()                          │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Plugin.Abstractions  (samostatná knihovna)                         │
│  - IPlugin, ITestablePlugin                                         │
│  - IWorklogUploadPlugin, IWorkSuggestionPlugin, IStatusIndicator…   │
│  - PluginBase, WorklogUploadPluginBase, …                           │
│  - PluginMetadata, PluginResult<T>, PluginConfigurationField        │
│  - ITokenProvider, ITokenProviderFactory                            │
└─────────────────────────────────────────────────────────────────────┘
          ▲                ▲                ▲                ▲
          │                │                │                │
┌─────────┴──────┐ ┌───────┴────────┐ ┌─────┴──────┐ ┌───────┴────────┐
│ Plugin         │ │ Plugin         │ │ Plugin     │ │ Plugin         │
│ .Atlassian     │ │ .GoranG3       │ │ .Luxafor   │ │ .Office365Cal. │
└────────────────┘ └────────────────┘ └────────────┘ └────────────────┘
```

---

## Vrstvy a odpovědnosti

### Domain (`src/WorkTracker.Domain`)

Čistý business model bez jakýchkoli externích závislostí. Obsahuje:

- **`WorkEntry`** — agregát (ticket, start/end, description, flags).
- **`IWorkEntryRepository`** — interface pro persistenci, implementace je v Infrastructure.
- **Business pravidla** — validace (start < end, nepřekrývající se intervaly), pomocné metody entity.

Domain layer **nezná** EF Core, plugins, DI, UI, nic. Je možné ji izolovaně unit‑testovat bez mockování.

### Application (`src/WorkTracker.Application`)

Orchestruje use cases. Závislá pouze na Domain a Plugin.Abstractions. Klíčové typy:

| Typ | Účel |
|-----|------|
| `IWorkEntryService` / `WorkEntryService` | Start/stop/edit/delete záznamů, detekce a řešení překryvů |
| `IWorklogSubmissionService` / `PluginBasedWorklogSubmissionService` | Pipeline odesílání worklogů přes pluginy |
| `IUnitOfWork` / `IUnitOfWorkFactory` | Transakční obal pro multi‑step zápisy |
| `IWorklogValidator`, `IDateRangeService` | Pomocné služby pro validaci a výpočet rozsahů |
| `ISecureStorage` | Abstrakce nad OS credential storem (implementace v Infrastructure) |
| `IPluginManager` | Registr a lifecycle pluginů (implementace v Infrastructure) |
| `Result<T>` | Výsledkový typ pro use cases (místo výjimek pro business chyby) |
| DTO (`WorklogDto`, `WorklogSubmissionDto`, `ProviderInfo`, `SubmissionResult`) | Přenosové typy mezi vrstvami |
| `WorkTrackerPaths` | Centralizované cesty (`%LocalAppData%/WorkTracker`, `keys/`, `logs/`, …) |
| `AppInfo` | Čtení verze z `AssemblyInformationalVersionAttribute` |

Registrace služeb: `DependencyInjection.AddApplication(IServiceCollection)`. Volá se automaticky z `AddInfrastructure`, takže prezentační vrstva volá jen `AddInfrastructure`.

### Infrastructure (`src/WorkTracker.Infrastructure`)

Implementace externích závislostí:

- **EF Core + SQLite** — `WorkTrackerDbContext`, `WorkEntryRepository`, `UnitOfWork`, `UnitOfWorkFactory`. Cesta k databázi se řídí `WorkTrackerPaths.DefaultDatabasePath` s možností přepsání přes `Database:Path` v configu.
- **`DbContextFactory`** — všechny repository používají `IDbContextFactory<WorkTrackerDbContext>`, ne scoped `DbContext`. Důvod: plugin manager a dlouho běžící služby (GUI) nemají HTTP request scope, takže scoped DbContext by vedl k leakům a concurrent‑use chybám.
- **`CredentialStoreSecureStorage`** — implementace `ISecureStorage` nad `GitCredentialManager` (multiplatformní: Windows Credential Manager, macOS Keychain, Linux libsecret).
- **`PluginManager`, `PluginLoader`, `PluginLoadContext`** — načítání pluginů z `AssemblyLoadContext` (isolated, collectible), DI scope per plugin.
- **`MsalTokenProviderFactory`, `MsalTokenProvider`** — MSAL Public Client s cross‑platform token cache (`Microsoft.Identity.Client.Extensions.Msal`) a **device code flow** pro interaktivní autentizaci.
- **`DependencyInjection.AddInfrastructure(IConfiguration)`** — jediný entry point pro registraci Application+Infrastructure služeb. Volá `AddApplication()` sám.

Infrastructure **závisí** na Application a Domain, **ne naopak**.

### UI.Shared (`src/WorkTracker.UI.Shared`)

Platformně neutrální UI logika:

- **ViewModely** (`MainWindowViewModel`, `SettingsViewModel`, `SuggestionsViewModel`, …) — `ObservableObject` z `CommunityToolkit.Mvvm`, `[RelayCommand]`, `[ObservableProperty]`. ViewModely jsou **úmyslně sdílené, ne per‑platform** (oproti pohledům/XAML, které jsou platformně specifické).
- **Orchestrátory** — třídy, které koordinují více služeb a skrývají workflow před ViewModely (např. `WorklogSubmissionOrchestrator` sestaví náhled, otevře dialog, provede submit, zpracuje chyby).
- **Služby**: `ISettingsService` (serializace `ApplicationSettings` do `settings.json`), `ILocalizationService` (resx loader s `INotifyPropertyChanged`), `IPomodoroService`.

UI.Shared závisí na Application, ne přímo na Infrastructure.

### Presentation

Tři nezávislé projekty:

- **`WorkTracker.CLI`** — `Host.CreateApplicationBuilder`, Serilog, Spectre.Console. Command dispatch je ručně v `Program.cs` (switch statement), implementace příkazů v `CommandHandler`.
- **`WorkTracker.WPF`** — Windows‑only, Material Design. `App.xaml.cs` staví `IHost`, registruje ViewModely a views.
- **`WorkTracker.Avalonia`** — cross‑platform. `App.axaml.cs` staví DI v background threadu po zobrazení splash okna, aby start byl vizuálně rychlý.

Všechny tři prezentační projekty volají `AddInfrastructure(configuration)` a pak si přidávají své vlastní UI služby.

---

## Dependency flow

Závislosti jdou **dovnitř**, nikdy ven:

```
Presentation ──> UI.Shared ──> Application ──> Domain
                                   │
                                   ▼
                          Plugin.Abstractions
                                   ▲
                                   │
                         (implementováno pluginy)
```

```
Infrastructure ──> Application ──> Domain
Infrastructure ──> Plugin.Abstractions
Presentation   ──> Infrastructure (jen pro AddInfrastructure)
```

Praktické důsledky:

- **Application nezná EF Core.** `WorkEntryService` používá `IWorkEntryRepository`, ne `DbContext`.
- **Presentation nezná pluginy přímo.** Komunikuje přes `IWorklogSubmissionService`, `IPluginManager`, což jsou Application interfaces.
- **Plugin.Abstractions je samostatný nuget‑like projekt**, aby externí pluginy nemusely referencovat celé Application.

---

## Presentation: CLI, WPF, Avalonia

Projekt záměrně udržuje **tři paralelní frontendy**. Důvody:

- **CLI** — rychlé skriptování, CI integrace, headless servery, automatizace.
- **WPF** — nejlepší integrace s Windows (tray, taskbar, notifikace přes `NotifyIcon`, jump lists).
- **Avalonia** — cross‑platform, stejná funkčnost jako WPF, do budoucna preferovaný frontend.

### ViewModely: hybridní přístup

**Root ViewModely** (například `MainViewModel`, `SettingsViewModel`) jsou v každém frontendu vlastní — WPF a Avalonia je nesdílejí. **To je záměr.** Drobné rozdíly v threadingu, messaging a styling bindings způsobí, že pokus o sdílený base class pro celé obrazovky končí kompromisy na obou stranách.

**Sub‑ViewModely**, které nemají framework‑specifickou vazbu, ale jsou naopak znovupoužitelné (například `SuggestionsViewModel`, pomodoro komponenty, plugin konfigurační VM), žijí v `WorkTracker.UI.Shared.ViewModels` a jsou **skládané** do root ViewModelů v obou frontendech. Vedle nich jsou v `UI.Shared` bezstavové služby, orchestrátory a settings.

### Dispatch mezi UI a služby

ViewModely volají orchestrátory nebo přímo Application služby. Orchestrátor je užitečný, když akce:

- má dialog (submit worklogu ukazuje náhled, pak modal s výběrem pluginu),
- kombinuje víc služeb (načti entries → validuj → otevři dialog → submit → zpracuj chyby),
- má retry/recovery logiku.

Bez orchestrátoru by toto všechno skončilo ve ViewModelu a duplikovalo se mezi WPF a Avalonia.

---

## Plugin systém

Detaily pro autory pluginů jsou v [plugin-development.md](plugin-development.md). Tady jen architektonické shrnutí.

### Plugin.Abstractions

Samostatná knihovna (`WorkTracker.Plugin.Abstractions.dll`) obsahující pouze veřejné API:

- `IPlugin`, `ITestablePlugin`
- `IWorklogUploadPlugin`, `IWorkSuggestionPlugin`, `IStatusIndicatorPlugin`
- Abstraktní base classes `PluginBase`, `WorklogUploadPluginBase`, `WorkSuggestionPluginBase`, `StatusIndicatorPluginBase`
- Datové typy: `PluginMetadata`, `PluginConfigurationField`, `PluginResult<T>`, `PluginErrorCategory`, `PluginValidationResult`
- Přenosové typy: `PluginWorklogEntry`, `WorklogSubmissionResult`, `WorklogSubmissionError`, `WorkSuggestion`, `StatusIndicatorState`
- Autentizace: `ITokenProvider`, `ITokenProviderFactory`

### Isolation: AssemblyLoadContext

Každý plugin se načítá do vlastního `PluginLoadContext` (dědí `AssemblyLoadContext(isCollectible: true)`):

- **Sdílené assembly** (`WorkTracker.Plugin.Abstractions`, `Microsoft.Extensions.*`, runtime) se vrací do Default contextu přes override `Load` → `null`, takže se nereferují duplicitně.
- **Ostatní závislosti** plugin nese vedle sebe ve své výstupní složce a `AssemblyDependencyResolver` je řeší lokálně.
- Kontext je **collectible** → lze ho unloadnout při `PluginManager.UnloadPluginAsync`.

Důsledek: plugin může používat jinou verzi závislosti než hlavní aplikace (pokud to závislost dovolí), a při chybě pluginu lze plugin shodit bez restartu aplikace.

### Plugin DI

`PluginManager` staví vlastní `ServiceCollection` pro pluginy, který obsahuje:

- `ILoggerFactory` / `ILogger<T>` (shared)
- `IHttpClientFactory`
- `ITokenProviderFactory` (MSAL)
- `ISecureStorage`

Plugin se pak instanciuje přes `ActivatorUtilities.CreateInstance` z tohoto scoped providera, takže konstruktor může brát libovolnou kombinaci těchto služeb. Žádné parameterless konstruktory.

### Discovery

`PluginLoader.DiscoverPluginFiles` skenuje adresáře a hledá soubory vyhovující `WorkTracker.Plugin.*.dll` (kromě `WorkTracker.Plugin.Abstractions.dll`, která je vyloučená v `DependencyInjection.InitializePluginsAsync`). Výchozí adresář: `{AppContext.BaseDirectory}/plugins`.

Kromě toho existují **embedded pluginy** — plugin knihovna přímo referencovaná z hlavní aplikace (bez dynamic loadu). Používá se to například v CLI, kde nechceme plugin adresář vedle binárky.

### Lifecycle

```
Discover → Load assembly do PluginLoadContext
        → Instantiate(DI)
        → Register in _loadedPlugins
        → InitializeAsync(config, ct)
            └ ValidateConfigurationAsync
            └ OnInitializeAsync (hook)
        → Ready to use

Shutdown/Unload:
        → ShutdownAsync
            └ OnShutdownAsync (hook)
            └ DisposeAsync
        → Remove from _loadedPlugins
        → PluginLoadContext.Unload()
```

`PluginBase` implementuje šablonové metody tak, že autor pluginu override‑uje jen hooks (`OnInitializeAsync`, `OnShutdownAsync`, `OnValidateConfigurationAsync`, `OnDisposeAsync`).

---

## Klíčové vzory

### Result pattern

Use cases nevrhají výjimky pro business chyby. Vrací `Result<T>`:

```csharp
public async Task<Result<WorkEntry>> StartWorkAsync(
    string? ticketId, DateTime? startTime, string? description,
    DateTime? endTime, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(ticketId) && string.IsNullOrWhiteSpace(description))
    {
        return Result<WorkEntry>.Failure("Ticket ID nebo popis je povinný.");
    }

    // …
    return Result<WorkEntry>.Success(entry);
}
```

Volající (ViewModel, CLI handler) prostě zkontroluje `IsSuccess`:

```csharp
var result = await _workEntryService.StartWorkAsync(…);
if (result.IsFailure)
{
    _notificationService.ShowError(result.Error);
    return;
}
```

**Výjimky** jsou vyhrazeny pro:
- programátorské chyby (`ArgumentNullException`, invarianty)
- skutečné infrastructure selhání (I/O, DbException) — ty propagují do handlerů a logují se přes Serilog

### Unit of Work

`IUnitOfWork` obaluje sérii zápisů do jedné EF Core transakce (`IDbContextTransaction`). Používá se pro multi‑step operace, kde částečný úspěch by nechal databázi v nekonzistentním stavu:

- **Auto‑stop předchozího + start nového** (CLI `start` s už běžícím záznamem)
- **Edit se vzniklým překryvem**, kdy aplikace zároveň ořízne kolidující záznam

```csharp
await using var uow = await _uowFactory.CreateAsync(ct);
await uow.WorkEntries.AddAsync(newEntry, ct);
await uow.WorkEntries.UpdateAsync(trimmedOldEntry, ct);
await uow.SaveChangesAsync(ct); // commit
```

Pokud `SaveChangesAsync` není zavoláno a `uow` se dispose‑ne, transakce se rollbackne. `WorkEntryRepository` má dva režimy:

- **Factory režim** (standalone) — každá operace si sama otevře `DbContext` z factory a rovnou commituje.
- **Shared‑context režim** (v rámci UoW) — operace přispívají do `DbContext` drženého UoW, SaveChanges je odložen na UoW.

`WorkEntryService` pro jednoduché operace používá transient repository přímo; pro vícekrokové zavolá UoW.

### Factory místo scoped DbContext

WorkTracker nepoužívá klasický scoped `DbContext` (jako v ASP.NET Core request scope), protože aplikace není request‑driven. Místo toho:

- `AddDbContextFactory<WorkTrackerDbContext>()` v DI
- Konzumenti injektují `IDbContextFactory<WorkTrackerDbContext>` a volají `CreateDbContext()` per operace
- Krátké using bloky → žádné leaky

### MVVM a Orchestrátory

ViewModely v UI.Shared jsou tenké — delegují akce na orchestrátory/services. Orchestrátor je stateless třída v Application layer nebo UI.Shared, která zapouzdřuje workflow s dialogy, validací a error recovery.

### Plugin Template Method

`PluginBase` je šablonová metoda:

```csharp
public virtual async Task<bool> InitializeAsync(
    IDictionary<string, string>? config, CancellationToken ct)
{
    Configuration = config ?? new Dictionary<string, string>();
    var validation = await ValidateConfigurationAsync(Configuration, ct);
    if (!validation.IsValid) { /* log, return false */ }

    var ok = await OnInitializeAsync(ct);    // hook
    IsInitialized = ok;
    return ok;
}
```

Autor pluginu override‑uje jen `OnInitializeAsync`, nemusí řešit validaci ani stav.

---

## Datový tok: příklad odeslání worklogu

Jako konkrétní ilustraci vezmeme tok **„Send today“** z Avalonia GUI do Tempa:

1. **UI** — Uživatel v `MainWindow` klikne na **Send today**. View zavolá `SubmitTodayCommand` ve `MainWindowViewModel`.
2. **ViewModel** — Deleguje na `WorklogSubmissionOrchestrator.SubmitDayAsync(DateTime.Today)`.
3. **Orchestrátor** — Zavolá `IWorklogSubmissionService.PreviewDailyWorklogAsync(today)` a otevře `SubmitWorklogDialog` s výsledkem.
4. **PluginBasedWorklogSubmissionService.PreviewDailyWorklogAsync**:
   - Načte `IEnumerable<WorkEntry>` přes `IWorkEntryService.GetWorkEntriesByDateAsync(today)`.
   - Mapuje přes `WorklogMapper` na `List<WorklogDto>`.
   - Validuje každý přes `IWorklogValidator` → odfiltruje neplatné.
   - Vrátí `WorklogSubmissionDto` (platné worklogy + důvody odfiltrovaných).
5. **Dialog** — Uživatel vidí náhled, vybere plugin (dropdown naplněný z `GetAvailableProviders`) a potvrdí.
6. **Orchestrátor** — Zavolá `SubmitDailyWorklogAsync(today, providerId)`.
7. **PluginBasedWorklogSubmissionService.SubmitDailyWorklogAsync**:
   - `ResolvePlugin(providerId)` → vrátí `IWorklogUploadPlugin`.
   - Pro každý validní `WorklogDto` vytvoří `PluginWorklogEntry`.
   - Zavolá `plugin.UploadWorklogsAsync(entries, ct)`.
8. **Plugin (Tempo)** —
   - `TempoWorklogPlugin` prochází záznamy v cyklu.
   - Pro každý: přeloží issue key → issue ID (s 1h cache), zavolá Tempo REST API `POST /worklogs`.
   - Retry logika (max 2 pokusy, backoff) na 408/429/500–504.
   - Vrátí `PluginResult<WorklogSubmissionResult>` s počty a případnými chybami.
9. **Zpět do služby** — mapuje `PluginResult<WorklogSubmissionResult>` → `Result<SubmissionResult>` (DTO layer).
10. **Orchestrátor** — Pokud success, zobrazí toast a refresh seznam. Pokud partial/failure, dialog zobrazí detaily chyb s tlačítkem **Retry failed**.

Celý tok demonstruje:

- **Vrstvy**: UI → Orchestrator → Application service → Plugin (přes abstraction).
- **Separation**: Application layer nezná HTTP nebo Tempo API — jen `IWorklogUploadPlugin`.
- **Result pattern**: na každé hranici se chyby propagují jako data, ne jako výjimky.
- **Plugin isolation**: Tempo implementace sedí v separátní DLL v `plugins/`, může být updatována nezávisle.
