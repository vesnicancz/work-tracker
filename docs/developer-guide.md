# Vývojářská příručka

Praktický průvodce pro vývojáře, kteří chtějí pracovat na samotném WorkTrackeru. Pokud tě zajímá, **jak je aplikace navržená**, začni v [architecture.md](architecture.md). Pokud píšeš plugin, jdi na [plugin-development.md](plugin-development.md).

---

## Obsah

1. [Prerekvizity](#prerekvizity)
2. [Naklonování a build](#naklonování-a-build)
3. [Struktura repozitáře](#struktura-repozitáře)
4. [Spouštění aplikací](#spouštění-aplikací)
5. [Testy](#testy)
6. [Migrace databáze](#migrace-databáze)
7. [Central Package Management](#central-package-management)
8. [Coding standards](#coding-standards)
9. [Unit of Work — kdy a jak](#unit-of-work--kdy-a-jak)
10. [Práce se secure storage](#práce-se-secure-storage)
11. [MSAL a device code flow](#msal-a-device-code-flow)
12. [Lokalizace](#lokalizace)
13. [Logging](#logging)
14. [CI/CD](#cicd)
15. [Release process](#release-process)
16. [Časté problémy při vývoji](#časté-problémy-při-vývoji)

---

## Prerekvizity

- **.NET 10 SDK** — projekt používá C# 14 (výchozí pro .NET 10, `LangVersion` se nenastavuje), nullable reference types a `TreatWarningsAsErrors = true`. Starší SDK build neprojde.
- **Git**
- **IDE** — Rider nebo Visual Studio 2022+. VS Code s C# Dev Kit také funguje.
- **SQLite browser** (volitelné) — DB Browser for SQLite nebo `sqlite3` CLI pro ad‑hoc inspekci `worktracker.db`.
- **EF Core CLI** — `dotnet tool install --global dotnet-ef` (pro migrace).

---

## Naklonování a build

```bash
git clone https://github.com/vesnicancz/work-tracker.git
cd work-tracker

dotnet restore
dotnet build
dotnet test
```

První build stáhne ~700 MB závislostí do standardní NuGet cache (typicky `~/.nuget/packages` na Linux/macOS nebo `%UserProfile%\.nuget\packages` na Windows). Konkrétní cesta závisí na tvé lokální konfiguraci NuGetu.

Build by měl projít **bez warningů** — projekt má `TreatWarningsAsErrors = true` v `Directory.Build.props`. Pokud něco bliká warningem, je to CI failure.

---

## Struktura repozitáře

```
work-tracker/
├── src/
│   ├── WorkTracker.Domain/               # Pure business model
│   ├── WorkTracker.Application/          # Use cases, Result<T>, DTOs
│   ├── WorkTracker.Infrastructure/       # EF Core, plugins, MSAL, secure storage
│   ├── WorkTracker.UI.Shared/            # Sdílené ViewModely, orchestrátory, služby
│   ├── WorkTracker.CLI/                  # Spectre.Console klient
│   ├── WorkTracker.Avalonia/             # Avalonia GUI (cross-platform)
│   └── WorkTracker.Plugin.Abstractions/  # Plugin API
├── plugins/
│   ├── WorkTracker.Plugin.Atlassian/
│   ├── WorkTracker.Plugin.Office365Calendar/
│   ├── WorkTracker.Plugin.GoranG3/
│   └── WorkTracker.Plugin.Luxafor/
├── tests/                                # xUnit + Moq + FluentAssertions
│   ├── WorkTracker.Domain.Tests/
│   ├── WorkTracker.Application.Tests/
│   ├── WorkTracker.Infrastructure.Tests/
│   ├── WorkTracker.UI.Shared.Tests/
│   ├── WorkTracker.Avalonia.Tests/
│   ├── WorkTracker.CLI.Tests/
│   ├── WorkTracker.Plugin.Atlassian.Tests/
│   ├── WorkTracker.Plugin.GoranG3.Tests/
│   ├── WorkTracker.Plugin.Luxafor.Tests/
│   ├── WorkTracker.Plugin.Office365Calendar.Tests/
│   └── WorkTracker.Tests.Common/         # Sdílené test helpery (WorkEntryBuilder, MockPluginFactory)
├── docs/
├── resources/
├── build/macos/                          # Info.plist, entitlements, make-app-bundle.sh
├── packages/                             # Lokální NuGet feed (DotLuxafor .nupkg)
├── .github/workflows/
│   ├── dotnet.yml                        # Build + test na PR/push
│   ├── release.yml                       # Multi-platform publish na git tag v*
│   └── assign-pr-author.yml              # Přiřadí PR jeho autorovi
├── Directory.Build.props                 # net10.0, Nullable, TreatWarningsAsErrors, MinVer
├── Directory.Packages.props              # Central Package Management
├── nuget.config                          # Zdroje balíčků (vč. lokálního packages/)
├── global.json                           # SDK pinning
└── WorkTracker.slnx                      # Solution (XML formát)
```

Solution je v novém `.slnx` formátu (XML, ne `.sln`). Aktuální Rider a Visual Studio 2022 ho otevřou přímo; starší verze potřebují převod.

---

## Spouštění aplikací

### CLI

```bash
dotnet run --project src/WorkTracker.CLI -- help
dotnet run --project src/WorkTracker.CLI -- start PROJ-123 "Bug fix"
```

Pozn.: `--` odděluje argumenty pro `dotnet run` od argumentů pro samotnou aplikaci.

### Avalonia

```bash
dotnet run --project src/WorkTracker.Avalonia
```

Při prvním spuštění se vytvoří databáze.

**Debug build běží automaticky v prostředí `Development`.** Oba entry pointy (`src/WorkTracker.Avalonia/Program.cs`, `src/WorkTracker.CLI/Program.cs`) nastaví `DOTNET_ENVIRONMENT=Development`, pokud proměnná není nastavená zvenčí — platí to pro IDE, `dotnet run` i ručně spuštěnou binárku. Debug běh tím dostane vlastní data a nemůže přepsat nic, co patří nainstalované aplikaci:

| | Debug (Development) | Release (Production) |
|---|---|---|
| Databáze | `bin/Debug/net10.0/worktracker-dev.db` | `Database:Path`, jinak `worktracker.db` v app data |
| Nastavení, logy, klíče | `…/WorkTracker_Development/` | `…/WorkTracker/` |
| Pluginy | `bin/Debug/.../plugins/` + `plugins/*/bin/Debug/net10.0/` | `plugins/` vedle binárky (v macOS `.app` `~/Library/Application Support/WorkTracker/plugins/`) |

Cesty pro Development definuje `appsettings.Development.json` (kopíruje se do outputu jen v Debug konfiguraci). Díky tomu debug běh vidí pluginy rovnou z jejich build outputu a není potřeba je nikam publikovat. Pustit debug build nad produkčními daty jde explicitním `DOTNET_ENVIRONMENT=Production`.

> **Proč to tak je:** debug build s prázdnou `plugins/` složkou dřív sdílel `settings.json` s nainstalovanou aplikací, a uložení nastavení z něj smazalo konfiguraci všech pluginů. Izolace prostředí je první obrana, `SettingsService` je druhá — viz `RestorePluginStateNobodySpokeFor`.

### Plugin projekty

Pluginy samy nejsou spustitelné. Build:

```bash
dotnet build plugins/WorkTracker.Plugin.Atlassian
```

Pro lokální testování v Avalonia nic dalšího není potřeba: Development konfigurace (`appsettings.Development.json`, `Plugins:Directories`) načítá všechny čtyři pluginy přímo z jejich `plugins/*/bin/Debug/net10.0`. Nový plugin tam stačí přidat do seznamu. `dotnet publish` pluginu do `bin/Debug/net10.0/plugins/` má smysl, jen když chceš vyzkoušet rozložení jako po instalaci.

---

## Testy

Projekt má přes 1 100 testů v xUnit v3 + Moq + FluentAssertions.

### Spuštění

```bash
# Vše
dotnet test

# Jeden projekt
dotnet test --project tests/WorkTracker.Application.Tests

# Jeden test (xUnit v3 filtry přes Microsoft.Testing.Platform)
dotnet test -- --filter-method "WorkTracker.Application.Tests.Services.WorkEntryServiceTests.StartWorkAsync_WithActiveEntry_ShouldAutoStopPrevious"

# Jen metoda/třída, filtr s wildcardem
dotnet test -- --filter-method "*OverlapResolution*"
```

> `dotnet test` běží od xUnit v3 4.0 v režimu Microsoft.Testing.Platform (opt-in v `global.json`, sekce `test`). Filtrování přes `--filter "FullyQualifiedName~..."` (VSTest) už není podporováno.

### Rozlišení unit vs. integration

- **Unit testy** (většina) — mock všech závislostí, bez DbContextu. `Domain.Tests`, `Application.Tests`, `UI.Shared.Tests`, `CLI.Tests` (Spectre.Console.Testing), plugin testy. `Avalonia.Tests` jsou headless UI testy (Avalonia.Headless).
- **Integration testy** (menšina) — `Infrastructure.Tests` používá in‑memory SQLite (`Microsoft.Data.Sqlite` s `:memory:` connection) pro reálný EF Core tok. Helper (`SqliteInMemoryFixture` + `TestDbContextFactory`) je v `WorkTracker.Infrastructure.Tests/Helpers`.

### xUnit konvence v projektu

- Třídy se jmenují `{SystemUnderTest}Tests`.
- Metody `{Method}_{Scenario}_{ExpectedResult}`, většinou ve tvaru `Should…`, např. `StartWorkAsync_WithoutTicketIdAndDescription_ShouldReturnFailure`.
- `[Fact]` pro jednotlivé případy, `[Theory]` + `[InlineData]` pro parametrizaci.
- **Žádné `[Setup]` jako v NUnitu** — xUnit používá konstruktor třídy a `IDisposable`/`IAsyncLifetime` pro cleanup. Fixtures (`IClassFixture<T>`) pro sdílený stav mezi testy ve třídě.
- `FluentAssertions` všude: `result.IsSuccess.Should().BeTrue();`, `entries.Should().HaveCount(3);`

### Mocking

Moq. Mocky se tvoří explicitně v konstruktoru testovací třídy, ne přes AutoMocker. Důvod: explicitní setup zviditelní závislosti SUT.

```csharp
public class WorkEntryServiceTests
{
    private readonly Mock<IWorkEntryRepository> _mockRepository = new();
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IUnitOfWorkFactory> _mockUnitOfWorkFactory = new();
    private readonly Mock<ILogger<WorkEntryService>> _mockLogger = new();
    private readonly WorkEntryService _service;

    public WorkEntryServiceTests()
    {
        _mockUnitOfWork.Setup(u => u.WorkEntries).Returns(_mockRepository.Object);
        _mockUnitOfWorkFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_mockUnitOfWork.Object);
        _service = new WorkEntryService(_mockRepository.Object, _mockUnitOfWorkFactory.Object, TimeProvider.System, _mockLogger.Object);
    }
}
```

---

## Migrace databáze

EF Core migrace se generují s Infrastructure jako target projektem a Avalonia jako startup projektem (jen ta referencuje `Microsoft.EntityFrameworkCore.Design`, který EF Tools ve startup projektu vyžadují):

```bash
# Nová migrace
dotnet ef migrations add AddNewColumn \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.Avalonia

# Aplikovat čekající migrace
dotnet ef database update \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.Avalonia

# Zpět o jednu migraci
dotnet ef database update PreviousMigrationName \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.Avalonia

# Vygenerovat SQL script (užitečné pro review)
dotnet ef migrations script \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.Avalonia
```

**Runtime migrace** — aplikace při startu volá `DependencyInjection.InitializeDatabaseAsync`, která spustí `DbContext.Database.MigrateAsync()`. Žádný ruční `dotnet ef database update` na produkci.

**Migration file naming** — EF Core sám přidává timestamp prefix. Název migrace je v PascalCase, dostatečně popisný (`AddIsActiveIndex`, ne `Fix1`).

**Code‑first** — model v `WorkTrackerDbContext.OnModelCreating`, migrace jsou generovaná z něho. Žádné ruční úpravy migration souborů kromě opravdu speciálních případů (data seedy, custom SQL).

---

## Central Package Management

Verze NuGet balíčků jsou centralizované v `Directory.Packages.props`. V `.csproj` se uvádí **pouze** jméno balíčku, bez verze:

```xml
<!-- ✅ správně -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />

<!-- ❌ špatně — verze je v Directory.Packages.props -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.0" />
```

Přidání nového balíčku:

1. Otevři `Directory.Packages.props`, přidej `<PackageVersion Include="..." Version="..." />`.
2. V `.csproj` projektu, který balíček potřebuje, přidej `<PackageReference Include="..." />`.
3. `dotnet restore`.

Update verzí:

```bash
# Zobrazit zastaralé
dotnet list package --outdated

# Update provést v Directory.Packages.props ručně,
# pak dotnet restore a dotnet test
```

---

## Coding standards

Konvence z `.editorconfig` + `Directory.Build.props`. Pravidla stylu a pojmenování z `.editorconfig` hlídá IDE — `EnforceCodeStyleInBuild` je vypnuté, takže je build nevynucuje:

- **C# 14**, **nullable reference types** enabled
- **`TreatWarningsAsErrors = true`** — compiler a analyzer warningy jsou errory (i v CI)
- **`_camelCase`** pro privátní fieldy, `PascalCase` pro typy, metody, public members
- **`IName`** pro interfaces
- **Tabs** pro indentaci (viz `.editorconfig`)
- **Curly braces povinné** i pro single‑line `if`/`else`/`for`/`while`:

```csharp
// ✅
if (result.IsFailure)
{
    return result;
}

// ❌
if (result.IsFailure) return result;
```

### Async

- Všechny I/O metody jsou async. `Task<T>` nebo `ValueTask<T>`.
- `CancellationToken` jako **poslední parametr**. V novém kódu bez default value (`= default`), pokud nemá volající opravdu co předat. Starší API (`IWorkEntryRepository`, `IWorkEntryService`, část Plugin.Abstractions) ho zatím mají.
- Pojmenování: metoda končí `Async`.

### Error handling

- **Business chyby** → `Result<T>` / `Result`, propagace nahoru bez výjimek.
- **Infrastructure chyby** (I/O, HTTP, DbException) → výjimka propaguje; zachycuje se až na hranici UI / CLI handleru a loguje se.
- **Programátorské chyby** (`null` tam, kde by neměl být, invarianty) → `ArgumentNullException` / `InvalidOperationException`. Žádné swallow.

### Žádné přehánění

Projekt striktně odmítá:

- Speculative abstractions „pro případ, že by to někdo chtěl“.
- Backwards‑compatibility shims pro necommitnuté/nereleasnuté věci.
- Feature flags, když stačí změnit kód.
- Wrapping trivialit do „služeb“ a `IXyzFactory` jen pro snadnější testování — pokud to nemá víc implementací, interface většinou nemusí existovat.

Když máš pochybnosti, volíme **menší, přímočařejší řešení**.

### Komentáře

- Jen tam, kde logika není samovysvětlující (proč, ne co).
- XML doc komentáře na **public API v Application a Plugin.Abstractions** — je to reference pro plugin autory i IntelliSense. Interní třídy komentáře nepotřebují.

---

## Unit of Work — kdy a jak

`IUnitOfWork` obalí víc zápisů do jedné EF Core transakce. Použij ho, když:

- Operace upravuje **víc než jeden záznam** a částečný úspěch by byl nekonzistentní.
- Potřebuješ **atomicitu** mezi repositoryi (typicky ještě není aplikovatelné, protože máme jen jedno `IWorkEntryRepository`, ale architektura je připravená).

### Příklad: start s auto‑stop běžících záznamů

Zjednodušeně podle `WorkEntryService.StartOpenEndedWorkAsync` — nový záznam bez konce uzavře všechno, co v okamžiku jeho začátku ještě běží (aktivní záznam i meeting s pevným koncem):

```csharp
var overlapping = await _repository.GetOverlappingEntriesAsync(null, workEntry.StartTime, null, ct);
var running = overlapping.Where(e => e.StartTime < workEntry.StartTime).ToList();
if (running.Count == 0)
{
    // Nic neběží — jediný zápis, UoW netřeba
    return Result.Success(await _repository.AddAsync(workEntry, ct));
}

await using var uow = await _unitOfWorkFactory.CreateAsync(ct);

// 1) Uzavři běžící záznamy v okamžiku začátku nového
foreach (var entry in running)
{
    entry.Stop(workEntry.StartTime, now);
    await uow.WorkEntries.UpdateAsync(entry, ct);
}

// 2) Přidej nový (vzniklý přes WorkEntry.Create(...))
var result = await uow.WorkEntries.AddAsync(workEntry, ct);

// 3) Commit — všechno nebo nic
await uow.SaveChangesAsync(ct);

return Result.Success(result);
```

Pokud UoW dispose‑uješ bez `SaveChangesAsync`, transakce se **rollbackne**.

### Single‑operation: bez UoW

Pro jednoduché CRUD operace se UoW nepoužívá. `WorkEntryService` dostane injekovanou transient `IWorkEntryRepository` (factory režim, auto‑save po každé operaci) a rovnou ji volá.

---

## Práce se secure storage

`ISecureStorage` abstrahuje nad OS credential storem. Implementace (`CredentialStoreSecureStorage`) používá `GitCredentialManager.ICredentialStore`, který multiplatformně řeší Windows Credential Manager, macOS Keychain a Linux libsecret.

### API

```csharp
public interface ISecureStorage
{
    string Protect(string plainText, string pluginId, string fieldKey);
    string Unprotect(string protectedText);
    void Remove(string pluginId, string fieldKey);
}
```

- `Protect` uloží `plainText` do OS storu pod targetem `worktracker://{pluginId}/{fieldKey}` a vrátí **placeholder** `CS:{pluginId}:{fieldKey}`, který se uloží do `settings.json`.
- `Unprotect` dostane placeholder nebo plaintext a vrátí plaintext (plaintext vrací beze změn, aby fungoval migration path).
- `Remove` smaže položku ze storu.

### Kdy se používá

- Při ukládání plugin konfigurace v `SettingsOrchestrator` (`ProtectSensitiveFields`) — pole typu `Password` se před serializací prožene `Protect`; prázdná hodnota položku ze storu odstraní (`Remove`).
- Při čtení (`SettingsService`, v CLI `PluginSettingsReader`) se pole prožene `Unprotect`, než se předá pluginu.

### Plugin sám nepotřebuje vědět

Plugin dostává ve `Configuration` už rozbalené plaintext hodnoty. Nemusí implementovat šifrování.

---

## MSAL a device code flow

Pluginy autentizující se přes Microsoft Entra ID používají **device code flow**. Důvod: `AcquireTokenInteractive` (přihlášení přes browser s callbackem) v Avalonia deadlockuje; device code flow žádný callback do aplikace nepotřebuje.

### Infrastructure: MsalTokenProviderFactory

`MsalTokenProviderFactory` je registrovaná jako singleton v plugin `ServiceCollection` uvnitř `PluginManager` (ne v aplikačním containeru) a vystavená pluginům přes `ITokenProviderFactory`. Plugin si vyžádá token provider:

```csharp
public class MyPlugin : WorkSuggestionPluginBase
{
    private readonly ITokenProviderFactory _tokenProviderFactory;
    private ITokenProvider? _tokenProvider;

    public MyPlugin(ILogger<MyPlugin> logger, ITokenProviderFactory tokenProviderFactory) : base(logger)
    {
        _tokenProviderFactory = tokenProviderFactory;
    }

    protected override async Task<bool> OnInitializeAsync(
        IDictionary<string, string> configuration,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredConfigValue("TenantId");
        var clientId = GetRequiredConfigValue("ClientId");
        var scopes = new[] { "Calendars.Read", "User.Read" };

        _tokenProvider = await _tokenProviderFactory.CreateAsync(tenantId, clientId, scopes);
        return true;
    }

    // Na žádost o token — sám nejdřív zkusí silent token z cache,
    // device code flow spustí, až když silent selže:
    private Task<string?> GetTokenAsync(IProgress<string>? progress, CancellationToken ct)
        => _tokenProvider!.AcquireTokenInteractiveAsync(progress, ct);
}
```

### Device code flow — co vidí uživatel

1. Plugin zavolá `AcquireTokenInteractiveAsync(progress, ct)`. Ten nejdřív zkusí `AcquireTokenSilentAsync`; když je token v cache, vrátí ho a další kroky odpadnou.
2. MSAL vygeneruje user code a verification URL.
3. `MsalTokenProvider` zavolá `progress?.Report("⏳ Open browser and enter code: ABC-123\nhttps://microsoft.com/devicelogin")`.
4. Zároveň se pokusí otevřít browser přes `Process.Start`.
5. Uživatel v browseru zadá code a přihlásí se.
6. MSAL mezitím pollne Entra a vrátí token.

### Token cache

- Cache soubor je v `%LocalAppData%\WorkTracker\keys\` s názvem `msal_{safeKey}.bin`, kde `safeKey = Convert.ToHexString(SHA256(tenantId:clientId))`.
- Šifrování: **DPAPI** (Windows), **Keychain** (macOS), **libsecret** (Linux) přes `Microsoft.Identity.Client.Extensions.Msal`.
- Fallback: pokud encrypted cache není dostupná (headless Linux bez secret service), MSAL použije nechráněný soubor `msal_{safeKey}_plain.bin` — `MsalTokenProviderFactory` zaloguje warning „Encrypted token cache not available, falling back to unprotected file“.

---

## Lokalizace

Texty UI jsou v `.resx` souborech v `src/WorkTracker.UI.Shared/Resources/Localization/`:

- `Strings.resx` — výchozí (angličtina)
- `Strings.cs.resx` — čeština

Který jazyk se použije, řídí `LanguageCatalog` (`UI.Shared/Models/`) — jediné místo, které převádí
uloženou hodnotu `ApplicationSettings.Language` na `CultureInfo`. Hodnota je `"system"` (následuj OS,
výchozí), nebo kód jazyka. Je to **string, ne enum**: neznámou hodnotu `LanguageCatalog.Normalize`
degraduje zpět na `"system"`, zatímco enum by shodil deserializaci a `SettingsService` by se svým
`catch` zahodil celý soubor nastavení.

`LocalizationService` si v konstruktoru uloží `SystemCulture` — tj. OS kulturu — protože setter
`CurrentCulture` přepisuje `CultureInfo.CurrentUICulture`, takže po prvním přepnutí už původní
hodnota není k dispozici. Volba `"system"` se pak resolvuje právě proti tomuto snapshotu.

### Přidání nového jazyka

1. Zkopíruj `Strings.resx` na `Strings.{culture}.resx` (např. `Strings.de.resx`).
2. Přelož hodnoty.
3. Přidej kód do `LanguageCatalog.SupportedLanguages` a nativní název do `LanguageCatalog.DisplayName`.
   (`LocalizationService.AvailableCultures` z katalogu vychází, takže se doplní samo.)
4. Build → `.resources.dll` se vygenerují automaticky do `bin/{Debug|Release}/{culture}/`.

### Použití v ViewModelech

Klíče jsou ploché (`Save`, `NoTicket`, …), bez prefixů. ViewModel dostane `ILocalizationService` přes konstruktor a sahá na indexer, pro texty s parametry na `GetFormattedString`:

```csharp
public string ActiveTicketDisplay => ActiveWork?.TicketId ?? _localization["NoTicket"];
```

V XAML se používá markup extension, která binduje na `LocalizationService.Instance` — `DataContext` nehraje roli:

```xml
<TextBlock Text="{markup:Localize Save}" />
```

### Přepínání jazyka za běhu

Uživatel jazyk volí v **Nastavení → Obecné → Jazyk** a změna se projeví okamžitě, bez restartu.
Mechanismus má dvě poloviny a obě jsou nutné:

- **XAML** — `{markup:Localize Key}` vrací binding na indexer `LocalizationService.Instance[Key]`.
  Ten se obnoví jen tehdy, když `PropertyChanged` nese jméno **`"Item"`**, tedy CLR název indexeru.
  Avalonia ho dohledává přes `GetDeclaredProperty`, takže prázdné jméno („všechno se změnilo")
  ani konvenční `"Item[]"` **nefungují** — indexer by zůstal viset ve starém jazyce.
  Hlídá to `LocalizeExtensionTests` a `LocalizationServiceTests.CurrentCulture_Changed_RaisesPropertyChangedForIndexer`.
- **C#** — cokoliv, co si přeložený text uloží do pole, se samo neobnoví. Taková místa se musí
  přihlásit k `ILocalizationService.PropertyChanged` a reagovat na **prázdné** jméno (to je signál
  „změnil se jazyk" pro běžné konzumenty). Dnes to dělá `TrayIconService` (nativní položky menu)
  a `MainViewModel` (počítané texty + cache fáze v `PomodoroViewModel`).

Jazyk se načte v `App.ReadEarlySettings()` a hned aplikuje přes `localization.ApplyLanguage(...)`,
tedy před konstrukcí prvního okna — jinak by při startu probliklo předchozí nastavení.

---

## Logging

Projekt používá **Serilog**, zapojený přes `Microsoft.Extensions.Logging` interface. Pluginy logují přes `ILogger<T>` a nevědí, že pod tím je Serilog.

### Konfigurace

V `CLI/Program.cs` (GUI v `Avalonia/App.axaml.cs` má totéž bez Console sinku a s `WorkTrackerPaths.LogFilePath`, tj. `worktracker-.log`):

```csharp
loggerConfiguration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(
        restrictedToMinimumLevel: LogEventLevel.Warning,
        standardErrorFromLevel: LogEventLevel.Warning)
    .WriteTo.File(
        WorkTrackerPaths.CliLogFilePath,  // %LocalAppData%\WorkTracker\logs\worktracker-cli-.log
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
```

### Konvence

- **`LogInformation`** pro high‑level události (start aplikace, plugin loaded, worklog submitted).
- **`LogWarning`** pro abnormality, které aplikace zvládne (plugin disabled, retry).
- **`LogError`** pro chyby (exception propagated, infrastructure failure) — vždy s exception jako prvním argumentem.
- **`LogDebug`** pro detail, který by byl v Infoverze spam.
- **Structured logging**: `_logger.LogInformation("Plugin {PluginId} loaded in {Elapsed} ms", pluginId, ms);` — parametry jako placeholdery, ne string interpolation.

### Kde najít logy

- GUI: `%LocalAppData%\WorkTracker\logs\worktracker-YYYYMMDD.log`
- CLI: `%LocalAppData%\WorkTracker\logs\worktracker-cli-YYYYMMDD.log`
- Retence: 14 souborů.

---

## CI/CD

### `.github/workflows/dotnet.yml`

Push + PR na `master`:

1. `actions/checkout@v6`
2. `actions/setup-dotnet@v5` s `.NET 10.0.x`
3. `dotnet restore`
4. `dotnet build --no-restore`
5. `dotnet test --no-build --verbosity normal`

Běží na `ubuntu-latest` — všechny projekty cílí na cross‑platform `net10.0`, takže build i testy projdou na Linuxu bez speciálního nastavení.

### `.github/workflows/release.yml`

Trigger: push tagu `v*`. Jobs:

1. **verify-tag** (ubuntu) — ověří, že tag je tříčíselná verze (`vX.Y.Z`, volitelně `-prerelease`). Jiný tag MinVer tiše ignoruje a release by vyšel pod číslem předchozího tagu.
2. **test** (ubuntu) — spustí celou test sadu před release.
3. **publish-cli** (matrix: win-x64, win-arm64, linux-x64, osx-arm64) — `dotnet publish` s `PublishSingleFile=true`, `SelfContained=false`, zip artifact.
4. **publish-avalonia** (matrix: win-x64, win-arm64, linux-x64, osx-arm64) — Avalonia pro všechny platformy. macOS varianta navíc projde krokem *Bundle and sign .app* a místo zipu se z ní balí `.dmg` (viz níže).

   Obě matice mají stejnou sadu RID: co dostane GUI, dostane i CLI. Intel Macy (`osx-x64`) se nebuildí — Macy se prodávají s Apple Siliconem od roku 2020 a na těch starších rozjede .NET host z `osx-arm64` buildu Rosetta.
5. **publish-plugins** (matrix: Atlassian, Luxafor, GoranG3, Office365Calendar) — každý plugin samostatně.
6. **release** — stáhne artifacty a vytvoří rovnou publikovaný GitHub Release s automaticky vygenerovanými poznámkami (`generate_release_notes`).

Artifacty jsou framework‑dependent (bez runtime). Kdo chce self‑contained, buildí si sám.

Názvy assetů v releasu jsou psané pro uživatele, ne podle RID: obsahují verzi z tagu (bez `v`) a čitelnou platformu (`windows-x64`, `windows-arm64`, `linux-x64`, `macos-arm64`, mapuje ji pole `platform` v matici):

| Job | Asset |
|---|---|
| publish-avalonia | `WorkTracker-1.15.0-windows-x64.zip`, …, `WorkTracker-1.15.0-macos-arm64.dmg` |
| publish-cli | `WorkTracker-CLI-1.15.0-windows-x64.zip`, … |
| publish-plugins | `WorkTracker-Plugin-Atlassian-1.15.0.zip`, … (složka uvnitř zůstává `WorkTracker.Plugin.Atlassian`) |

Interní názvy Actions artifactů (`matrix.artifact`) se do releasu nepropisují.

#### macOS `.app` bundle

GUI pro macOS není holá binárka jako na ostatních platformách, ale `WorkTracker.app` (CLI pro macOS zůstává zip s binárkou). Bundle není kosmetika — bez něj Finder ani Dock nemají jméno a ikonu, tray ikona a notifikační backend (`UNUserNotificationCenter`) nemají bundle identifier, na který by se navázaly, a Apple Silicon nepodepsanou binárku odmítne spustit úplně.

Sestavuje ho `build/macos/make-app-bundle.sh` (běží jen na macOS runneru, potřebuje `sips`, `iconutil`, `plutil` a `codesign`):

```
WorkTracker.app/
└── Contents/
    ├── Info.plist              # z build/macos/Info.plist, @VERSION@ se nahradí z tagu
    ├── MacOS/                  # jen apphost a nativní knihovny (.dylib/.so)
    └── Resources/
        ├── appsettings.json    # zbytek publish outputu
        └── WorkTracker.icns    # vygenerováno z resources/app-ico.png
```

**Do `Contents/MacOS/` patří výhradně kód.** `codesign` každý soubor v tomto adresáři pečetí jako vnořený code object, takže obyčejný datový soubor shodí `codesign --verify --strict` hláškou `code object is not signed at all / In subcomponent: …`. Skript proto publish output rozděluje: spustitelná binárka a nativní knihovny do `MacOS/`, všechno ostatní do `Resources/`.

Aplikace tomu jde naproti dvojicí cest ve `WorkTrackerPaths` — mimo bundle obě vracejí `AppContext.BaseDirectory`, uvnitř bundlu se rozejdou:

| Cesta | V bundlu | K čemu |
|---|---|---|
| `AppContentDirectory` | `Contents/Resources` | co se distribuuje s aplikací — `appsettings.json` |
| `WritableBaseDirectory` | `~/Library/Application Support/WorkTracker` | co aplikace zakládá — `plugins/`, relativní cesta k databázi |

Do podepsaného bundlu se nesmí zapisovat, a plugin adresář se při startu zakládá (`Directory.CreateDirectory`) — na macOS proto pluginy nepatří dovnitř `.app`, ale do `~/Library/Application Support/WorkTracker/plugins/`.

`CFBundleIdentifier` je `io.github.vesnicancz.WorkTracker`, tedy stejná identita jako `DesktopEntry.AppId` na Linuxu. `CFBundleShortVersionString` snese jen tři čísla oddělená tečkou, takže se z tagu odřízne prefix `v` i prerelease část (`v1.14.0-beta.1` → `1.14.0`).

Do releasu jde bundle jako disk image `WorkTracker-{verze}-macos-arm64.dmg`, ne jako zip: po otevření ukáže okno s aplikací a zástupcem `Applications`, takže instalace je jedno přetažení, jak to uživatel na Macu čeká. Workflow nechá skript postavit bundle rovnou do `publish/dmg/`, přidá k němu symlink na `/Applications` a `hdiutil create -format UDZO` z té složky udělá svazek beze změny — symlinky a podpis bundlu přežijí. `hdiutil` na hostovaných runnerech občas selže s `Resource busy`, proto ho krok zkouší až třikrát.

**Podpis je ad-hoc** (`codesign --sign -`). To je maximum bez placeného Apple účtu: aplikace se na Apple Silicon vůbec spustí a nehlásí „is damaged", ale **není to průchod Gatekeeperem** — stažený build pořád chce jedno ruční povolení: od macOS 15 v *Nastavení systému → Soukromí a zabezpečení → Přesto otevřít*, na starších *pravý klik → Otevřít*, kdekoli `xattr -dr com.apple.quarantine /Applications/WorkTracker.app`.

Přechod na skutečný podpis: naimportuj Developer ID certifikát do keychainu runneru a v kroku *Bundle and sign .app* předej skriptu `CODESIGN_IDENTITY` s jeho jménem přes `env:` (třeba ze secretu) — dnes tam workflow posílá jen `VERSION_TAG`. Skript pak sám přidá hardened runtime a `build/macos/entitlements.plist` (CoreCLR JIT potřebuje zapisovatelně‑spustitelnou paměť a single‑file host rozbaluje nepodepsané nativní knihovny). Zbývat bude už jen poslat archiv do `notarytool` a přilepit staple.

---

## Release process

Verzi neurčuje žádný soubor v repu — počítá ji **MinVer** z git tagu (`MinVerTagPrefix` = `v` v `Directory.Build.props`). Nic se ručně nebumpuje.

1. **Ujisti se**, že commit, který chceš vydat, je na `master`.
2. **Vytvoř tag** — tříčíselný `vMAJOR.MINOR.PATCH`, volitelně s prerelease částí (`v1.2.3-beta.1`):

   ```bash
   git tag v1.2.3
   git push origin v1.2.3
   ```

3. **CI** spustí `release.yml`: job `verify-tag` odmítne tag v jiném tvaru, pak proběhnou testy, publish všech platforem a vznikne rovnou publikovaný GitHub Release s artifacty.
4. **Release notes** vygeneruje GitHub automaticky (`generate_release_notes`). Pokud chceš, uprav je po vydání ručně.

---

## Časté problémy při vývoji

### Warning se tváří jako error

`TreatWarningsAsErrors = true` = každý warning fail buildu. Nejčastější:

- **CS8600 / CS8603** — nullability. Oprava: `?`, `!`, nebo skutečná null guard.
- **CS0618** — obsolete API. Obvykle je v error message už předepsaný migration path.
- **Analyzer warning** (vestavěné .NET analyzéry, `AnalysisLevel = latest`) — nech analyzer vyjet v IDE, oprav podle hintu.

### `file is locked` při buildu GUI projektu

Zavři všechny běžící instance aplikace (i ty v tray), pak `dotnet build` znovu.

### Plugin se po publishi neobjevuje

1. Zkontroluj, že `.dll` skutečně je v plugin adresáři: výchozí `plugins/` vedle binárky (na macOS `~/Library/Application Support/WorkTracker/plugins/`), případně v adresářích z `Plugins:Directories`. Prohledávají se i podsložky.
2. Plugin musí **začínat prefixem** `WorkTracker.Plugin.` (loader filtruje podle jména souboru, `WorkTracker.Plugin.Abstractions.dll` přeskakuje).
3. DLL musí obsahovat **non‑abstract třídu implementující `IPlugin`** (v praxi jedno z `IWorklogUploadPlugin` / `IWorkSuggestionPlugin` / `IStatusIndicatorPlugin`). Abstraktní třídy a interfaces jsou přeskočené.
4. V logu najdeš selhání načtení (`Failed to load plugin …`, `No plugin types found in …`) i úspěšně načtené pluginy (`Loaded plugin: …`). Jednotlivé pokusy (`Loading plugin from …`) jsou jen na úrovni Debug, takže se do logu nezapíšou.
5. Nově nalezený plugin je ve výchozím stavu **vypnutý** — zapni ho v Nastavení → Pluginy.

### EF Core migrace fail: `No DbContext was found`

`DbContext` pro EF Tools vytváří `WorkTrackerDbContextFactory` (`IDesignTimeDbContextFactory`) v Infrastructure. Startup projekt ale musí referencovat `Microsoft.EntityFrameworkCore.Design` — to dnes dělá jen `WorkTracker.Avalonia`, proto `--startup-project src/WorkTracker.Avalonia`. S CLI jako startup projektem příkaz selže hláškou „Your startup project 'WorkTracker.CLI' doesn't reference Microsoft.EntityFrameworkCore.Design“.

### Rider nevidí `.slnx`

Aktualizuj Rider na aktuální verzi, nebo otevři jednotlivé projekty přes `File → Open` a vybrat `.csproj`.

### Avalonia XAML: „Binding path is not type‑safe“

Avalonia compiled bindings odhalí type mismatch už v XAML parseru. Pokud komponenta má složitější DataContext, extrahuj ji do samostatného `UserControl` s explicitním `x:DataType` — jinak si scope naleje typy z parenta a kompilátor řve.

### Git hooks / pre‑commit

Projekt nemá husky ani lefthook. Všechny kontroly běží v CI. Před push si ale udělej:

```bash
dotnet build
dotnet test
```
