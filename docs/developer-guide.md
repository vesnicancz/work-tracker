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

- **.NET 10 SDK** — projekt používá C# 13, nullable reference types a `TreatWarningsAsErrors = true`. Starší SDK build neprojde.
- **Git**
- **IDE** — Rider nebo Visual Studio 2022+. VS Code s C# Dev Kit také funguje.
- **SQLite browser** (volitelné) — DB Browser for SQLite nebo `sqlite3` CLI pro ad‑hoc inspekci `worktracker.db`.
- **Windows** pro WPF projekt; Linux/macOS pro zbytek.
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
│   ├── WorkTracker.WPF/                  # WPF GUI (Windows)
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
│   ├── WorkTracker.Plugin.Atlassian.Tests/
│   ├── WorkTracker.Plugin.GoranG3.Tests/
│   ├── WorkTracker.Plugin.Luxafor.Tests/
│   ├── WorkTracker.Plugin.Office365Calendar.Tests/
│   └── WorkTracker.Tests.Common/         # Sdílené test helpery, in-memory DbContext
├── docs/
├── resources/
├── .github/workflows/
│   ├── dotnet.yml                        # Build + test na PR/push
│   └── release.yml                       # Multi-platform publish na git tag v*
├── Directory.Build.props                 # Nullable, warnings as errors, C# 13
├── Directory.Packages.props              # Central Package Management
├── global.json                           # SDK pinning
└── WorkTracker.slnx                      # Solution (XML formát)
```

Solution je v novém `.slnx` formátu (XML, ne `.sln`). Rider 2024.x+ a VS 17.12+ ho otevřou přímo; starší verze potřebují převod.

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

Při prvním spuštění se vytvoří databáze. Pluginy se discoverují z adresáře vedle binárky — při debugování je to `src/WorkTracker.Avalonia/bin/Debug/net10.0/plugins/`, kam se plugin dostane buď přes `dotnet publish` plugin projektu do té složky, nebo ručním zkopírováním DLL.

### WPF

```bash
dotnet run --project src/WorkTracker.WPF
```

Pouze Windows (target `net10.0-windows`).

### Plugin projekty

Pluginy samy nejsou spustitelné. Build:

```bash
dotnet build plugins/WorkTracker.Plugin.Atlassian
```

A pro lokální testování v Avalonia / WPF:

```bash
dotnet publish plugins/WorkTracker.Plugin.Atlassian -c Debug \
  -o src/WorkTracker.Avalonia/bin/Debug/net10.0/plugins/Atlassian
```

---

## Testy

Projekt má ~700 testů v xUnit + Moq + FluentAssertions.

### Spuštění

```bash
# Vše
dotnet test

# Jeden projekt
dotnet test tests/WorkTracker.Application.Tests

# Jeden test
dotnet test --filter "FullyQualifiedName~WorkEntryServiceTests.StartWorkAsync_WithActiveEntry_StopsPrevious"

# Jen metoda/třída, filtr částečný
dotnet test --filter "FullyQualifiedName~OverlapResolution"

# S coverage (Coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Rozlišení unit vs. integration

- **Unit testy** (většina) — mock všech závislostí, bez DbContextu. `Domain.Tests`, `Application.Tests`, `UI.Shared.Tests`, plugin testy.
- **Integration testy** (menšina) — `Infrastructure.Tests` používá in‑memory SQLite (`Microsoft.Data.Sqlite` s `:memory:` connection) pro reálný EF Core tok. Helper pro stvoření `DbContextFactory` je v `WorkTracker.Tests.Common`.

### xUnit konvence v projektu

- Třídy se jmenují `{SystemUnderTest}Tests`.
- Metody `{Method}_{Scenario}_{ExpectedResult}`, např. `StartWorkAsync_WithEmptyTicketAndDescription_ReturnsFailure`.
- `[Fact]` pro jednotlivé případy, `[Theory]` + `[InlineData]` pro parametrizaci.
- **Žádné `[Setup]` jako v NUnitu** — xUnit používá konstruktor třídy a `IDisposable`/`IAsyncLifetime` pro cleanup. Fixtures (`IClassFixture<T>`) pro sdílený stav mezi testy ve třídě.
- `FluentAssertions` všude: `result.IsSuccess.Should().BeTrue();`, `entries.Should().HaveCount(3);`

### Mocking

Moq. Mocky se tvoří explicitně v konstruktoru testovací třídy, ne přes AutoMocker. Důvod: explicitní setup zviditelní závislosti SUT.

```csharp
public class WorkEntryServiceTests
{
    private readonly Mock<IWorkEntryRepository> _repoMock = new();
    private readonly Mock<IUnitOfWorkFactory> _uowFactoryMock = new();
    private readonly WorkEntryService _sut;

    public WorkEntryServiceTests()
    {
        _sut = new WorkEntryService(_repoMock.Object, _uowFactoryMock.Object, TimeProvider.System);
    }
}
```

---

## Migrace databáze

EF Core migrace se generují s Infrastructure jako target projektem a CLI jako startup projektem:

```bash
# Nová migrace
dotnet ef migrations add AddNewColumn \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI

# Aplikovat (smaže + znovu z migrations)
dotnet ef database update \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI

# Zpět o jednu migraci
dotnet ef database update PreviousMigrationName \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI

# Vygenerovat SQL script (užitečné pro review)
dotnet ef migrations script \
  --project src/WorkTracker.Infrastructure \
  --startup-project src/WorkTracker.CLI
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

Konvence vynucené přes `.editorconfig` + `Directory.Build.props`:

- **C# 13**, **nullable reference types** enabled
- **`TreatWarningsAsErrors = true`** — warningy jsou errory v CI
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
- `CancellationToken` jako **poslední parametr**, bez default value (`= default`) kromě okrajových případů, kde nemá volající co předat.
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

### Příklad: start s auto‑stop předchozího záznamu

```csharp
public async Task<Result<WorkEntry>> StartWorkAsync(
    string? ticketId, DateTime? startTime, string? description,
    DateTime? endTime, CancellationToken ct)
{
    // …validace…

    var effectiveStart = startTime ?? _timeProvider.GetLocalNow().DateTime;

    await using var uow = await _uowFactory.CreateAsync(ct);

    // 1) Zastav aktivní záznam, pokud existuje
    var active = await uow.WorkEntries.GetActiveAsync(ct);
    if (active != null)
    {
        active.EndTime = effectiveStart;
        active.IsActive = false;
        await uow.WorkEntries.UpdateAsync(active, ct);
    }

    // 2) Vytvoř nový
    var entry = new WorkEntry
    {
        TicketId = ticketId,
        Description = description,
        StartTime = effectiveStart,
        IsActive = endTime == null,
        EndTime = endTime,
    };
    await uow.WorkEntries.AddAsync(entry, ct);

    // 3) Commit — oboje nebo nic
    await uow.SaveChangesAsync(ct);

    return Result<WorkEntry>.Success(entry);
}
```

Pokud metodu dispose‑uješ bez `SaveChangesAsync`, transakce se **rollbackne**.

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

- Při ukládání plugin konfigurace v `SettingsService` — pole typu `Password` (nebo API tokeny) se před serializací prožene `Protect`.
- Při čtení před předáním pluginu se pole prožene `Unprotect`.

### Plugin sám nepotřebuje vědět

Plugin dostává ve `Configuration` už rozbalené plaintext hodnoty. Nemusí implementovat šifrování.

---

## MSAL a device code flow

Pluginy autentizující se přes Microsoft Entra ID používají **device code flow**. Důvod viz [architecture.md](architecture.md) a paměť o MSAL deadlocku v Avalonia.

### Infrastructure: MsalTokenProviderFactory

`MsalTokenProviderFactory` je registrovaná jako singleton v DI a vystavená pluginům přes `ITokenProviderFactory`. Plugin si vyžádá token provider:

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

    // Na žádost o token:
    private async Task<string?> GetTokenAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var token = await _tokenProvider!.AcquireTokenSilentAsync(ct);
        if (token != null)
        {
            return token;
        }

        // Silent selhalo (nebo cache prázdná) — spusť device code flow
        return await _tokenProvider.AcquireTokenInteractiveAsync(progress, ct);
    }
}
```

### Device code flow — co vidí uživatel

1. Plugin zavolá `AcquireTokenInteractiveAsync(progress, ct)`.
2. MSAL vygeneruje user code a verification URL.
3. `MsalTokenProvider` zavolá `progress?.Report("Go to https://microsoft.com/devicelogin and enter code ABC-123")`.
4. Zároveň se pokusí otevřít browser přes `Process.Start`.
5. Uživatel v browseru zadá code a přihlásí se.
6. MSAL mezitím pollne Entra a vrátí token.

### Token cache

- Cache soubor je v `%LocalAppData%\WorkTracker\keys\` s názvem `msal_{safeKey}.bin`, kde `safeKey = Convert.ToHexString(SHA256(tenantId:clientId))`.
- Šifrování: **DPAPI** (Windows), **Keychain** (macOS), **libsecret** (Linux) přes `Microsoft.Identity.Client.Extensions.Msal`.
- Fallback: pokud encrypted cache není dostupná (headless Linux bez secret service), MSAL použije nechráněný soubor `msal_{safeKey}_plain.bin` — plugin zaloguje warning.

---

## Lokalizace

Texty UI jsou v `.resx` souborech v `src/WorkTracker.UI.Shared/Localization/`:

- `Strings.resx` — výchozí (angličtina)
- `Strings.cs.resx` — čeština

### Přidání nového jazyka

1. Zkopíruj `Strings.resx` na `Strings.{culture}.resx` (např. `Strings.de.resx`).
2. Přelož hodnoty.
3. Přidej kulturu do `LocalizationService.AvailableCultures`.
4. Build → `.resources.dll` se vygenerují automaticky do `bin/{Debug|Release}/{culture}/`.

### Použití v ViewModelech

```csharp
public partial class SettingsViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public string SaveButtonText => _loc["Settings.Save"];
}
```

V XAML se bindí přes indexer a `ILocalizationService` jako `DataContext`:

```xml
<Button Content="{Binding Loc[Settings.Save]}" />
```

Služba implementuje `INotifyPropertyChanged`, takže přepnutí jazyka v runtime regeneruje všechny bindingy.

---

## Logging

Projekt používá **Serilog**, zapojený přes `Microsoft.Extensions.Logging` interface. Pluginy logují přes `ILogger<T>` a nevědí, že pod tím je Serilog.

### Konfigurace

V `CLI/Program.cs` a `Avalonia/App.axaml.cs`:

```csharp
loggerConfiguration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Warning)
    .WriteTo.File(
        WorkTrackerPaths.LogFilePath,  // %LocalAppData%\WorkTracker\logs\worktracker-.log
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14);
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

1. `actions/checkout@v5`
2. `actions/setup-dotnet@v5` s `.NET 10.0.x`
3. `dotnet restore`
4. `dotnet build --no-restore`
5. `dotnet test --no-build --verbosity normal`

Běží na `ubuntu-latest`. WPF projekt se **neúčastní** build matrixu — je `net10.0-windows` a ubuntu image ho přeskočí.

### `.github/workflows/release.yml`

Trigger: push tagu `v*`. Jobs:

1. **test** (ubuntu) — spustí celou test sadu před release.
2. **publish-cli** (matrix: win-x64, linux-x64, osx-x64, osx-arm64) — `dotnet publish` s `PublishSingleFile=true`, `SelfContained=false`, zip artifact.
3. **publish-wpf** (windows-latest) — WPF jen pro Windows, zip přes `Compress-Archive`.
4. **publish-avalonia** (matrix: win-x64, linux-x64, osx-x64, osx-arm64, win-arm64) — Avalonia pro všechny platformy.
5. **publish-plugins** (matrix: Atlassian, Luxafor, GoranG3, Office365Calendar) — každý plugin samostatně.
6. **release** — stáhne artifacty a vytvoří GitHub Release.

Artifacty jsou framework‑dependent (bez runtime). Kdo chce self‑contained, buildí si sám.

---

## Release process

1. **Aktualizuj verzi** v `Directory.Build.props`:

   ```xml
   <Version>1.2.3</Version>
   <InformationalVersion>1.2.3</InformationalVersion>
   ```

2. **Commit + push** na master.
3. **Vytvoř tag**:

   ```bash
   git tag v1.2.3
   git push origin v1.2.3
   ```

4. **CI** spustí `release.yml`, zkompiluje všechny platformy a vytvoří GitHub Release s artifacty.
5. **Release notes** — edituj rovnou na GitHubu po vytvoření releasu (CI vytváří draft / prázdný release). Dělíme podle: Features, Fixes, Plugins, Chores.

---

## Časté problémy při vývoji

### Warning se tváří jako error

`TreatWarningsAsErrors = true` = každý warning fail buildu. Nejčastější:

- **CS8600 / CS8603** — nullability. Oprava: `?`, `!`, nebo skutečná null guard.
- **CS0618** — obsolete API. Obvykle je v error message už předepsaný migration path.
- **Analyzer warning** (StyleCop / Roslynator) — nech analyzer vyjet v IDE, oprav podle hintu.

### `file is locked` při buildu WPF projektu

Zavři všechny instance WPF aplikace (i ty v tray), pak `dotnet build` znovu.

### Plugin se po publishi neobjevuje

1. Zkontroluj, že `.dll` skutečně je v `plugins/` vedle binárky. `PluginLoader` scanuje jen tam.
2. Plugin musí **začínat prefixem** `WorkTracker.Plugin.` (loader filtruje podle jména souboru).
3. Plugin musí implementovat jedno z `IWorklogUploadPlugin` / `IWorkSuggestionPlugin` / `IStatusIndicatorPlugin` **jako non‑abstract třídu**. Abstraktní třídy jsou přeskočené.
4. V logu `PluginLoader` loguje každý soubor, který zkusil načíst, i důvod selhání.

### EF Core migrace fail: `No DbContext was found`

EF Tools potřebují startup projekt s DI bootstrap, který vytvoří `DbContext`. Proto používáme `--startup-project src/WorkTracker.CLI`. CLI volá `AddInfrastructure`, který registruje `DbContextFactory`, a EF Tools to najdou.

### Rider nevidí `.slnx`

Aktualizuj na Rider 2024.2+, nebo otevři jednotlivé projekty přes `File → Open` a vybrat `.csproj`.

### Avalonia XAML: „Binding path is not type‑safe“

Avalonia compiled bindings odhalí type mismatch už v XAML parseru. Pokud komponenta má složitější DataContext, extrahuj ji do samostatného `UserControl` s explicitním `x:DataType` — jinak si scope naleje typy z parenta a kompilátor řve.

### Git hooks / pre‑commit

Projekt nemá husky ani lefthook. Všechny kontroly běží v CI. Před push si ale udělej:

```bash
dotnet build
dotnet test
```
