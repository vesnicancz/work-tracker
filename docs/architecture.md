# Architektura

WorkTracker je postavený na **Clean Architecture** se čtyřmi vrstvami a plugin systémem. Tento dokument popisuje, jak jsou vrstvy uspořádané, jak komunikují a jaké vzory projekt používá.

Pokud tě zajímá **jak napsat vlastní plugin**, jdi rovnou na [plugin-development.md](plugin-development.md). Pokud hledáš **setup, build a testy**, viz [developer-guide.md](developer-guide.md).

---

## Obsah

1. [Diagram vrstev](#diagram-vrstev)
2. [Vrstvy a odpovědnosti](#vrstvy-a-odpovědnosti)
3. [Dependency flow](#dependency-flow)
4. [Presentation: CLI, Avalonia](#presentation-cli-avalonia)
5. [Plugin systém](#plugin-systém)
6. [Klíčové vzory](#klíčové-vzory)
7. [Datový tok: odeslání worklogů z GUI](#datový-tok-odeslání-worklogů-z-gui)

---

## Diagram vrstev

```
┌─────────────────────────────────────────────────────────────────────┐
│  Presentation                                                       │
│  ┌──────────────┐  ┌──────────────────────────┐                     │
│  │ WorkTracker  │  │ WorkTracker.Avalonia     │                     │
│  │ .CLI         │  │ (cross-platform desktop) │                     │
│  └──────┬───────┘  └──────────┬───────────────┘                     │
│         │                     │                                     │
│         │         ┌───────────┘                                     │
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
│  - Business pravidla (validace časů, plán řešení překryvů)          │
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

- **`WorkEntry`** — agregát (`TicketId`, `StartTime`/`EndTime`, `Description`, `IsActive`). Settery jsou privátní, záznam vzniká přes `WorkEntry.Create(...)` a mění se přes metody (`Stop`, …).
- **`IWorkEntryRepository`** — interface pro persistenci, implementace je v Infrastructure.
- **Business pravidla** — `WorkEntry.IsValid()` (ticket nebo popis je povinný, konec nesmí být před začátkem) a `OverlapResolver`, který spočítá plán úprav kolidujících záznamů (`OverlapResolutionPlan`). Samotnou kontrolu překryvů proti databázi dělá `WorkEntryService` přes repository.

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

- **EF Core + SQLite** — `WorkTrackerDbContext`, `WorkEntryRepository`, `UnitOfWork`, `UnitOfWorkFactory`. Cesta k databázi se řídí `WorkTrackerPaths.DefaultDatabasePath` s možností přepsání přes `Database:Path` v configu (relativní cesty se vyhodnocují vůči `WorkTrackerPaths.WritableBaseDirectory` — adresáři aplikace, uvnitř macOS `.app` bundlu vůči `~/Library/Application Support/WorkTracker/`). `Database:Pooling` (default `true`) vypíná connection pooling pro databáze na removable médiích — bez poolingu se soubor zavírá po každé operaci a disk jde bezpečně vysunout. Při startu se vynucuje `PRAGMA journal_mode=DELETE` (WAL je na removable exFAT rizikový kvůli `-wal`/`-shm` souborům). Když databáze není dosažitelná (nepřipojený disk, IO chyba, read-only médium), `AddInfrastructure` i `InitializeDatabaseAsync` vyhodí `DatabaseUnavailableException` — Avalonia `App` na ni reaguje dialogem s tlačítkem *Zkusit znovu*, které zopakuje celý bootstrap; ostatní výjimky při startu jsou brány jako neopravitelné.
- **`DbContextFactory`** — všechny repository používají `IDbContextFactory<WorkTrackerDbContext>`, ne scoped `DbContext`. Důvod: plugin manager a dlouho běžící služby (GUI) nemají HTTP request scope, takže scoped DbContext by vedl k leakům a concurrent‑use chybám.
- **`CredentialStoreSecureStorage`** — implementace `ISecureStorage` nad `GitCredentialManager` (multiplatformní: Windows Credential Manager, macOS Keychain, Linux libsecret).
- **`PluginManager`, `PluginLoader`, `PluginLoadContext`** — načítání pluginů do vlastního `AssemblyLoadContext` (collectible) pro každou plugin assembly; všechny pluginy sdílejí jeden plugin `ServiceProvider`.
- **`MsalTokenProviderFactory`, `MsalTokenProvider`** — MSAL Public Client s cross‑platform token cache (`Microsoft.Identity.Client.Extensions.Msal`) a **device code flow** pro interaktivní autentizaci.
- **`DependencyInjection.AddInfrastructure(IConfiguration)`** — jediný entry point pro registraci Application+Infrastructure služeb. Volá `AddApplication()` sám.

Infrastructure **závisí** na Application a Domain, **ne naopak**.

### UI.Shared (`src/WorkTracker.UI.Shared`)

Platformně neutrální UI logika:

- **Sub‑ViewModely** (`SuggestionsViewModel`, `PomodoroViewModel`, `PluginViewModel`, `ConfigurationFieldViewModel`, …) — `ObservableObject` z `CommunityToolkit.Mvvm`, příkazy jako `AsyncRelayCommand`/`RelayCommand` vytvořené v konstruktoru, vlastnosti přes `SetProperty` (source generátory `[RelayCommand]`/`[ObservableProperty]` projekt nepoužívá). Root ViewModely jsou v projektu Avalonia, viz [ViewModely: hybridní přístup](#viewmodely-hybridní-přístup).
- **Orchestrátory** — třídy, které koordinují více služeb a skrývají workflow před ViewModely (např. `WorklogSubmissionOrchestrator` sestaví náhled včetně agregace, provede submit a retry a označí položky, které plugin odmítl). Dialogy otevírá Avalonia `DialogService`, ne orchestrátor.
- **Služby**: `ISettingsService` (serializace `ApplicationSettings` do `settings.json`), `ILocalizationService` (resx loader s `INotifyPropertyChanged`), `IPomodoroService`.

UI.Shared závisí na Application, ne přímo na Infrastructure.

### Presentation

Dva nezávislé projekty:

- **`WorkTracker.CLI`** — `Host.CreateApplicationBuilder`, Serilog, Spectre.Console. Command dispatch je ručně v `Program.cs` (switch statement), implementace příkazů v `CommandHandler`.
- **`WorkTracker.Avalonia`** — cross‑platform. `App.axaml.cs` nejdřív zobrazí prázdné nastylované hlavní okno (se `StartMinimized` žádné) a DI host pak staví v background threadu (`Task.Run`), aby start byl vizuálně rychlý.

Oba prezentační projekty volají `AddInfrastructure(configuration)` a pak si přidávají své vlastní UI služby.

---

## Dependency flow

Závislosti jdou **dovnitř**, nikdy ven:

```
Avalonia ──> UI.Shared ──> Application ──> Domain
CLI ─────────────────────> Application
                               │
                               ▼
                      Plugin.Abstractions
                               ▲
                               │
                     (implementováno pluginy)
```

```
Infrastructure ──> Application ──> Domain
Infrastructure ──> Plugin.Abstractions (tranzitivně přes Application)
Presentation   ──> Infrastructure (bootstrap: AddInfrastructure, InitializeDatabaseAsync,
                                   InitializePluginsAsync, DatabaseUnavailableException)
```

Praktické důsledky:

- **Application nezná EF Core.** `WorkEntryService` používá `IWorkEntryRepository`, ne `DbContext`.
- **Presentation nezná implementace pluginů.** Komunikuje přes `IWorklogSubmissionService`, `IPluginManager` (Application interfaces) a pracuje jen s typy z Plugin.Abstractions (`WorklogSubmissionMode`, `PluginConfigurationField`, …).
- **Plugin.Abstractions je samostatný nuget‑like projekt**, aby externí pluginy nemusely referencovat celé Application.

---

## Presentation: CLI, Avalonia

Projekt záměrně udržuje **dva paralelní frontendy**. Důvody:

- **CLI** — rychlé skriptování, CI integrace, headless servery, automatizace.
- **Avalonia** — cross‑platform desktop GUI (Windows, Linux, macOS).

Dříve existoval i třetí frontend **WPF** (Windows‑only); byl odstraněn, protože Avalonia jeho funkčnost plně pokrývá i na Windows a duplicitní údržba se nevyplácela.

### ViewModely: hybridní přístup

**Root ViewModely** (například `MainViewModel`, `SettingsViewModel`) jsou frontend‑specifické a žijí přímo v projektu `WorkTracker.Avalonia`. **To je záměr.** Framework‑specifické detaily (threading, messaging, styling bindings) patří k frontendu; sdílený base class pro celé obrazovky by končil kompromisy.

**Sub‑ViewModely**, které nemají framework‑specifickou vazbu, ale jsou naopak znovupoužitelné (například `SuggestionsViewModel`, pomodoro komponenty, plugin konfigurační VM), žijí v `WorkTracker.UI.Shared.ViewModels` a jsou **skládané** do root ViewModelů. Vedle nich jsou v `UI.Shared` bezstavové služby, orchestrátory a settings.

### Dispatch mezi UI a služby

ViewModely volají orchestrátory nebo přímo Application služby. Orchestrátor je užitečný, když akce:

- má dialog (submit worklogu ukazuje náhled, pak modal s výběrem pluginu),
- kombinuje víc služeb (načti entries → validuj → otevři dialog → submit → zpracuj chyby),
- má retry/recovery logiku.

Bez orchestrátoru by toto všechno skončilo ve ViewModelu a orchestrace by se míchala s prezentační logikou.

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
- Enum kapacit: `WorklogSubmissionMode` (`[Flags]` — `Timed`, `Aggregated`) pro inzerci režimů, které plugin přijímá
- Autentizace: `ITokenProvider`, `ITokenProviderFactory`

### Isolation: AssemblyLoadContext

Každý plugin se načítá do vlastního `PluginLoadContext` (dědí `AssemblyLoadContext(isCollectible: true)`):

- **Sdílené assembly** — pro každou assembly, která už je načtená v Default contextu (typicky `WorkTracker.Plugin.Abstractions`, `Microsoft.Extensions.*`, runtime), vrátí override `Load` → `null` a použije se hostitelova kopie. Porovnává se jen jméno (`AssemblyName.ReferenceMatchesDefinition`), ne verze. Pevný seznam neexistuje — sdílí se cokoli, co hostitel v tu chvíli načetl.
- **Ostatní závislosti** plugin nese vedle sebe ve své výstupní složce a `AssemblyDependencyResolver` je řeší lokálně.
- Kontext je **collectible** → lze ho unloadnout přes `PluginManager.UnloadPluginAsync`. Aplikace to dnes dělá jen při ukončení (`UnloadAllPluginsAsync`); vypnutí pluginu v Nastavení ho jen odfiltruje a výměna DLL vyžaduje restart.

Důsledek: plugin může mít vlastní verzi závislosti, kterou hostitel nenačetl. U assembly, kterou už hostitel načetl, dostane vždy hostitelovu verzi.

### Plugin DI

`PluginManager` staví vlastní `ServiceCollection` pro pluginy, který obsahuje:

- `ILoggerFactory` / `ILogger<T>` (shared)
- `IHttpClientFactory`
- `ITokenProviderFactory` (MSAL)

Jeden `ServiceProvider` (`BuildServiceProvider()`) sdílejí všechny pluginy. Plugin se instanciuje přes `ActivatorUtilities.CreateInstance` z tohoto providera, takže konstruktor může brát libovolnou kombinaci těchto služeb. Žádné parameterless konstruktory.

### Discovery

`PluginLoader.DiscoverPluginFiles` rekurzivně (`SearchOption.AllDirectories`) skenuje adresáře a hledá soubory vyhovující `WorkTracker.Plugin.*.dll`; `WorkTracker.Plugin.Abstractions.dll` vyřazuje přímo tato metoda. Adresáře se berou z `Plugins:Directories`, výchozí je `WorkTrackerPaths.DefaultPluginsPath` (`{WritableBaseDirectory}/plugins`).

`PluginManager` interně podporuje i registraci zabudovaného pluginu přes `LoadEmbeddedPlugin<T>()`, ale aktuální bootstrap v `InitializePluginsAsync` tuto cestu nepoužívá — všechny pluginy jsou načítané dynamickým discovery z adresáře.

### Lifecycle

```
Discover → Load assembly do PluginLoadContext
        → Instantiate(DI)
        → Register in _loadedPlugins
        → InitializeAsync(config, ct)   (jen povolené pluginy; nový plugin je vypnutý)
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
    string? ticketId, DateTime? startTime = null, string? description = null,
    DateTime? endTime = null, CancellationToken cancellationToken = default)
{
    var workEntry = WorkEntry.Create(ticketId, start, end, description, roundedNow);
    if (!workEntry.IsValid())
    {
        return Result.Failure<WorkEntry>(InvalidEntryError);
    }

    // …
    return Result.Success(added);
}
```

Statické továrny jsou na negenerickém `Result`: `Result.Success()`, `Result.Failure(error)`, `Result.Success(value)` a `Result.Failure<T>(error)`.

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

- **Auto‑stop + start nového** — start záznamu bez konce, zatímco jiný běží (CLI i GUI, `WorkEntryService.StartOpenEndedWorkAsync`). Zavře se aktivní záznam i rozběhnutý meeting s nastaveným koncem.
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
- Konzumenti injektují `IDbContextFactory<WorkTrackerDbContext>` a volají `CreateDbContextAsync()` per operace
- Krátké using bloky → žádné leaky

### MVVM a Orchestrátory

ViewModely jsou tenké — delegují akce na orchestrátory/services. Orchestrátory žijí v `UI.Shared/Orchestrators` a zapouzdřují workflow s validací a error recovery; dialogy samotné otevírá frontend (`IDialogService`). Většina je bez stavu, výjimkou je třeba `CachedWorkSuggestionOrchestrator`, který si drží cache.

### Plugin Template Method

`PluginBase` je šablonová metoda:

```csharp
public virtual async Task<bool> InitializeAsync(
    IDictionary<string, string>? config, CancellationToken ct)
{
    Configuration = config ?? new Dictionary<string, string>();
    var validation = await ValidateConfigurationAsync(Configuration, ct);
    if (!validation.IsValid) { /* log, return false */ }

    var ok = await OnInitializeAsync(Configuration, ct);    // hook
    IsInitialized = ok;
    return ok;
}
```

Autor pluginu override‑uje jen `OnInitializeAsync`, nemusí řešit validaci ani stav.

---

## Datový tok: odeslání worklogů z GUI

Jako konkrétní ilustraci vezmeme odeslání záznamů jednoho dne z Avalonia GUI do Tempa:

1. **UI** — Uživatel v postranním panelu `MainWindow` klikne na **Odeslat záznamy práce** (`SubmitWorklogs`). Tlačítko je navázané na `SubmitWorklogCommand` v `MainViewModel`.
2. **Root ViewModel** — `MainViewModel.SubmitWorklogAsync` zavolá `IDialogService.ShowSubmitWorklogDialogAsync(SelectedDate, false)`, tedy pro den vybraný v hlavním okně.
3. **DialogService** — Vytvoří `SubmitWorklogViewModel`, zavolá jeho `InitializeAsync(date, isWeek)` a otevře modální `SubmitWorklogDialog`.
4. **Dialog ViewModel** — `InitializeAsync` načte náhled přes `WorklogSubmissionOrchestrator.LoadPreviewAsync(date, isWeekly, mode, …)`, který zavolá `IWorklogSubmissionService.PreviewDailyWorklogAsync(date)` (týdenní náhled `PreviewWeeklyWorklogAsync`).
5. **PluginBasedWorklogSubmissionService.PreviewDailyWorklogAsync**:
   - Načte záznamy dne přes `IWorkEntryService.GetWorkEntriesByDateAsync(date)`.
   - Ponechá jen dokončené (s `EndTime`), běžící záznam do náhledu nejde.
   - Převede je na `WorklogDto` (`DurationMinutes` zaokrouhleno nahoru, minimálně 1).
   - Vrátí `WorklogSubmissionDto { SubmissionDate, Worklogs }`.
6. **Dialog** — Uživatel vidí náhled, vybere plugin (dropdown obsahuje všechny povolené upload pluginy, `GetAvailableProviders`) a **submission mode** (Timed / Aggregated; radio button módu, který `ProviderInfo.SupportedModes` neobsahuje, je disabled přes `CanUseTimedMode` / `CanUseAggregatedMode`) a potvrdí. Volba se persistuje per plugin: `ApplicationSettings.SubmissionModeByProvider` (pluginId → mode) plus `LastSubmissionProviderId` pro plugin, na kterém se dialog otevře; `LastSubmissionMode` zůstává globálním fallbackem pro plugin, který v mapě ještě není. Výběr pluginu přehodí mode na ten jeho (`ResolveModeFor`), nepodporovaný zapamatovaný mode se ignoruje. V **Aggregated** módu orchestrátor před zobrazením preview seskupí DTO podle `(TicketId, Description)` per den a součtem nastaví `DurationMinutes`; `StartTime` skupiny = nejstarší start.
7. **Orchestrátor** — Na tlačítko **Odeslat** zavolá `SubmitWorklogViewModel` metodu `WorklogSubmissionOrchestrator.SubmitAsync`, která zaškrtnuté položky převede zpět na `WorklogDto` a zavolá `IWorklogSubmissionService.SubmitCustomWorklogsAsync(worklogs, providerId, mode, ct)`. (`SubmitDailyWorklogAsync` / `SubmitWeeklyWorklogAsync` používá jen CLI `send`, drží Timed default a jako jediné prochází `IWorklogValidator`.)
8. **PluginBasedWorklogSubmissionService.SubmitCustomWorklogsAsync**:
   - Validuje, že `mode` je single value (Timed nebo Aggregated) — jinak `Failure`.
   - `ResolvePlugin(providerId)` → vrátí `IWorklogUploadPlugin`.
   - Ověří, že plugin daný mode podporuje (`plugin.SupportedModes.HasFlag(mode)`).
   - Převede všechny `WorklogDto` přes `WorklogMapper.ToPluginWorklogs()` na `PluginWorklogEntry` (bez další validace).
   - Zavolá `plugin.UploadWorklogsAsync(entries, mode, ct)`.
9. **Plugin (Tempo)** —
   - `TempoWorklogPlugin` prochází záznamy v cyklu (podporuje oba módy: `SupportedModes = Timed | Aggregated`).
   - Pro každý: přeloží issue key → issue ID (s 1h cache), zavolá Tempo REST API `POST /worklogs`.
   - V **Timed** módu payload obsahuje `startDate + startTime + timeSpentSeconds`; v **Aggregated** módu plugin `startTime` z payloadu zcela vynechá (Tempo ho má jako volitelné pole).
   - Retry logika: až 2 opakování (celkem 3 pokusy, backoff 2 s a 4 s) na 408/429/500/502/503/504.
   - Vrátí `PluginResult<WorklogSubmissionResult>` s počty a případnými chybami.
10. **Zpět do služby** — mapuje `PluginResult<WorklogSubmissionResult>` → `Result<SubmissionResult>` (DTO layer).
11. **Orchestrátor a dialog** — `SubmitAsync` podle `SubmissionResult.Errors` označí chybné položky (`MarkFailedItems`) a vrátí `SubmissionOutcome`. Při úplném úspěchu se dialog zavře a `MainViewModel` zobrazí toast. Při částečném nebo úplném selhání dialog zůstane otevřený, chybné řádky zčervenají a tlačítko **Znovu odeslat** (`RetryFailedCommand` → `RetryFailedAsync`) pošle znovu zaškrtnuté chybné položky.

Celý tok demonstruje:

- **Vrstvy**: UI → Orchestrator → Application service → Plugin (přes abstraction).
- **Separation**: Application layer nezná HTTP nebo Tempo API — jen `IWorklogUploadPlugin`.
- **Result pattern**: na každé hranici se chyby propagují jako data, ne jako výjimky.
- **Plugin isolation**: Tempo implementace sedí v separátní DLL v `plugins/`, může být updatována nezávisle.
