# Vývoj pluginů

Průvodce pro autory pluginů. Pokud tě zajímá **jak plugin systém funguje interně** (AssemblyLoadContext, DI, lifecycle), viz [architecture.md § Plugin systém](architecture.md#plugin-systém).

---

## Obsah

1. [Co je plugin](#co-je-plugin)
2. [Tři typy pluginů](#tři-typy-pluginů)
3. [Quick start: minimální plugin](#quick-start-minimální-plugin)
4. [PluginBase a base classes](#pluginbase-a-base-classes)
5. [Metadata](#metadata)
6. [Konfigurace](#konfigurace)
7. [ITestablePlugin a test connection](#itestableplugin-a-test-connection)
8. [Plugin result a kategorizace chyb](#plugin-result-a-kategorizace-chyb)
9. [Autentizace: ITokenProviderFactory a device code flow](#autentizace-itokenproviderfactory-a-device-code-flow)
10. [Dependency injection v pluginech](#dependency-injection-v-pluginech)
11. [Balení a deployment](#balení-a-deployment)
12. [Testování pluginu](#testování-pluginu)
13. [Referenční pluginy](#referenční-pluginy)

---

## Co je plugin

Plugin je samostatná .NET knihovna (`.dll`), která implementuje jedno nebo více rozhraní z `WorkTracker.Plugin.Abstractions` a přidává do aplikace nové schopnosti:

- Odesílání worklogů do externího systému (Tempo, Goran G3, jiný ERP)
- Návrhy úkolů z externího zdroje (Jira, kalendář, ticketing systém)
- Ovládání fyzických indikátorů (LED, status tabule)

Plugin žije ve své vlastní izolované assembly, aby:

- **Neznečišťoval** hlavní aplikaci závislostmi (např. Atlassian plugin nese svůj HTTP klient, Luxafor svou HID knihovnu).
- **Šlo updatovat nezávisle** na jádru aplikace.
- **Šel uvolnit z paměti** — `AssemblyLoadContext` pluginu je collectible a `IPluginManager.UnloadPluginAsync` existuje. Aplikace ho ale zatím volá jen při ukončení; vypnutí pluginu v Nastavení ho jen vyřadí z používání a výměna DLL vyžaduje restart.
- **Neshazoval** hlavní aplikaci svými chybami.

Pluginy se při startu načtou z adresářů v `Plugins:Directories` (`appsettings.json`). Výchozí je `plugins/` vedle binárky hlavní aplikace; uvnitř macOS `.app` bundlu je to `~/Library/Application Support/WorkTracker/plugins/`, protože do podepsaného bundlu se nezapisuje. V Debug buildu (Development) se navíc načítají pluginy z repa přímo z jejich `bin/Debug/net10.0` (viz `appsettings.Development.json`).

Nově nalezený plugin je **ve výchozím stavu vypnutý** — uživatel ho musí povolit v **Nastavení → Pluginy**.

---

## Tři typy pluginů

| Typ | Rozhraní | Base class | Kdy použít |
|-----|----------|------------|------------|
| **Worklog Upload** | `IWorklogUploadPlugin` | `WorklogUploadPluginBase` | Chceš odesílat / číst worklogy z externího systému. |
| **Work Suggestion** | `IWorkSuggestionPlugin` | `WorkSuggestionPluginBase` | Chceš aplikaci dodat seznam úkolů k zobrazení v Suggestions dialogu. |
| **Status Indicator** | `IStatusIndicatorPlugin` | `StatusIndicatorPluginBase` | Chceš reagovat na Pomodoro fáze ovládáním externího zařízení. |

Jedna třída může implementovat **víc** rozhraní (např. Luxafor: `StatusIndicatorPluginBase` + `ITestablePlugin`) a jedna DLL může obsahovat **víc** pluginových tříd — loader instanciuje každou neabstraktní třídu implementující `IPlugin`. Každá je pak samostatný plugin s vlastním `Id` (např. `Plugin.Atlassian` obsahuje `TempoWorklogPlugin` a `JiraSuggestionsPlugin`, které sdílí `JiraClient`).

---

## Quick start: minimální plugin

Vytvoříme jednoduchý worklog upload plugin, který „odešle“ záznamy do souboru na disku.

### 1. Projekt

```bash
dotnet new classlib -n WorkTracker.Plugin.FileDump -o plugins/WorkTracker.Plugin.FileDump
cd plugins/WorkTracker.Plugin.FileDump
```

V `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Zkopíruje NuGet závislosti do výstupu buildu, takže plugin jde načíst přímo z bin/ -->
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\WorkTracker.Plugin.Abstractions\WorkTracker.Plugin.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

Stejně vypadají csproj všech referenčních pluginů. `TargetFramework` (`net10.0`), `Nullable` a `ImplicitUsings` dodá v repu `Directory.Build.props`, verze balíčků `Directory.Packages.props`; mimo repo si je doplň sám. `Microsoft.Extensions.Logging.Abstractions` přijde tranzitivně přes `WorkTracker.Plugin.Abstractions`.

> **Kopie `WorkTracker.Plugin.Abstractions.dll` vedle pluginu nevadí.** Loader ji při hledání pluginů přeskočí a `PluginLoadContext` vezme každou assembly, kterou už má načtenou hlavní aplikace, z Default kontextu — `IPlugin` z pluginu je tak vždy ten samý typ jako v aplikaci (viz [§ Balení a deployment](#balení-a-deployment)). `<Private>false</Private>` na referenci by jen zmenšilo balíček.

### 2. Třída pluginu

```csharp
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.FileDump;

public sealed class FileDumpWorklogPlugin : WorklogUploadPluginBase
{
    public FileDumpWorklogPlugin(ILogger<FileDumpWorklogPlugin> logger) : base(logger)
    {
    }

    public override PluginMetadata Metadata => new()
    {
        Id = "filedump.worklog",
        Name = "File Dump",
        Version = new Version(1, 0, 0),
        Author = "Jan Novák",
        Description = "Zapisuje worklogy do NDJSON souboru (jeden JSON objekt na řádek).",
        Tags = ["file", "export", "worklog"],
    };

    public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() =>
    [
        new PluginConfigurationField
        {
            Key = "OutputPath",
            Label = "Cesta k souboru",
            Type = PluginConfigurationFieldType.Text,
            IsRequired = true,
            Placeholder = @"C:\temp\worklogs.ndjson",
        },
    ];

    public override async Task<PluginResult<bool>> TestConnectionAsync(
        IProgress<string>? progress, CancellationToken ct)
    {
        var path = GetRequiredConfigValue("OutputPath");
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && !Directory.Exists(dir))
            {
                return PluginResult<bool>.Failure(
                    $"Adresář {dir} neexistuje.",
                    PluginErrorCategory.Validation);
            }

            // Ověříme, že do cílového souboru můžeme zapisovat (prázdný NDJSON soubor je validní — nic se nepřidává).
            await using (File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                // no-op — otevři/zavři jen pro ověření zápisu
            }
            return PluginResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Test connection selhal");
            return PluginResult<bool>.Failure(ex.Message, PluginErrorCategory.Internal);
        }
    }

    public override async Task<PluginResult<bool>> UploadWorklogAsync(
        PluginWorklogEntry worklog, CancellationToken ct)
    {
        var path = GetRequiredConfigValue("OutputPath");
        try
        {
            // NDJSON — každý záznam je samostatný JSON objekt na svém řádku,
            // takže append-only zápis je bezpečný a soubor zůstane line-delimited.
            var line = System.Text.Json.JsonSerializer.Serialize(worklog) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, ct);
            return PluginResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Upload worklog selhal pro ticket {Ticket}", worklog.TicketId);
            return PluginResult<bool>.Failure(ex.Message, PluginErrorCategory.Internal);
        }
    }

    public override Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(
        DateTime startDate, DateTime endDate, CancellationToken ct) =>
        Task.FromResult(PluginResult<IEnumerable<PluginWorklogEntry>>.Success(
            Enumerable.Empty<PluginWorklogEntry>()));

    public override Task<PluginResult<bool>> WorklogExistsAsync(
        PluginWorklogEntry worklog, CancellationToken ct) =>
        Task.FromResult(PluginResult<bool>.Success(false));
}
```

### 3. Build a nasazení

V Debug buildu načítá Avalonia pluginy z adresářů v `src/WorkTracker.Avalonia/appsettings.Development.json` (`Plugins:Directories`). Přidej tam výstup nového pluginu a stačí ho buildnout:

```json
"../../../../../plugins/WorkTracker.Plugin.FileDump/bin/Debug/net10.0"
```

```bash
dotnet build plugins/WorkTracker.Plugin.FileDump
```

Druhá možnost je publikovat plugin do `plugins/` vedle binárky aplikace:

```bash
dotnet publish plugins/WorkTracker.Plugin.FileDump -c Debug \
  -o src/WorkTracker.Avalonia/bin/Debug/net10.0/plugins/WorkTracker.Plugin.FileDump
```

Spusť Avalonia aplikaci — plugin se objeví v **Nastavení → Pluginy** jako „File Dump“, zatím vypnutý. Vyber ho, zaškrtni **Povolit plugin**, zadej cestu k souboru, klikni na **Otestovat připojení** (test běží s neuloženými hodnotami z formuláře) a ulož. Pak v hlavním okně klikni na **Odeslat záznamy práce**, vyber „File Dump“ a odešli.

---

## PluginBase a base classes

Neimplementuj `IPlugin` přímo. Použij **base class** — ušetří ti práci s validací, stavem, disposením a loggingem.

### `PluginBase`

Abstraktní třída pro **všechny** pluginy. `IPlugin` sám dědí `IAsyncDisposable`, takže `PluginBase` dostává `DisposeAsync` automaticky. Klíčové members:

```csharp
public abstract class PluginBase(ILogger logger) : IPlugin
{
    protected ILogger Logger { get; }
    protected IDictionary<string, string> Configuration { get; }
    protected bool IsInitialized { get; }

    // Override:
    public abstract PluginMetadata Metadata { get; }
    public abstract IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

    // Hooks — přepiš jen to, co potřebuješ:
    protected virtual Task<bool> OnInitializeAsync(
        IDictionary<string, string> configuration,
        CancellationToken cancellationToken) => Task.FromResult(true);
    protected virtual Task OnShutdownAsync() => Task.CompletedTask;
    protected virtual Task<PluginValidationResult> OnValidateConfigurationAsync(
        IDictionary<string, string> configuration,
        CancellationToken cancellationToken) => /* default: vrací Success(); hook pro dodatečná pravidla nad rámec základní validace (IsRequired, u povinných polí i regex), kterou dělá ValidateConfigurationAsync před zavoláním tohoto hooku */;
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    // Helpery:
    protected string? GetConfigValue(string key);
    protected string GetRequiredConfigValue(string key);  // throws if missing
    protected void EnsureInitialized();  // throws if not
}
```

> `OnInitializeAsync` dostává `configuration` dict jako parametr. V rámci hooků můžeš použít buď tento parametr, nebo `base.Configuration` (jsou identické — base class si dict uloží před voláním hooku). Používej `GetRequiredConfigValue` / `GetConfigValue`, které čtou z `Configuration` a dávají konzistentní error reporting.

**Výchozí validace** (`ValidateConfigurationAsync`, běží při každé `InitializeAsync`) prochází **jen pole s `IsRequired = true`** a u nich kontroluje:
- hodnota je přítomná a neprázdná,
- pokud má pole `ValidationPattern`, hodnota mu odpovídá (regex s timeoutem 1 s); chybová hláška je z `ValidationMessage`.

U nepovinných polí se `ValidationPattern` zatím **nevyhodnocuje**. Formát volitelné hodnoty proto kontroluj v `OnValidateConfigurationAsync` (viz [§ Vlastní validace](#vlastní-validace)).

### `WorklogUploadPluginBase`

Dědí `PluginBase` a implementuje `IWorklogUploadPlugin`. Definuje abstraktní metody:

- `TestConnectionAsync(IProgress<string>?, CancellationToken)`
- `UploadWorklogAsync(PluginWorklogEntry, CancellationToken)`
- `GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken)`
- `WorklogExistsAsync(PluginWorklogEntry, CancellationToken)`

Virtual metoda `UploadWorklogsAsync(IEnumerable<PluginWorklogEntry>, WorklogSubmissionMode, CancellationToken)` má **výchozí implementaci**, která prochází záznamy jeden po druhém, volá `UploadWorklogAsync` a sbírá chyby do `WorklogSubmissionResult`. Stačí přepsat, jen pokud chceš hromadné API nebo potřebuješ jiný payload podle módu (např. Tempo v Aggregated vynechává `startTime`).

#### `SupportedModes`

Virtual property `WorklogSubmissionMode SupportedModes` inzeruje, které submission módy plugin umí. **Výchozí hodnota je `Timed`** — pokud plugin zvládá i agregované záznamy (grupování podle kódu+popisu per den bez reálného intervalu), override na `Timed | Aggregated`:

```csharp
public override WorklogSubmissionMode SupportedModes =>
    WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated;
```

V **Aggregated** módu plugin dostává `PluginWorklogEntry`, kde:
- `StartTime` je reprezentativní timestamp (nejstarší start skupiny), ne reálný interval.
- `EndTime == StartTime` (není smysluplný).
- `DurationMinutes` = součet trvání všech záznamů ve skupině (autoritativní zdroj pravdy pro čas).

Submission service i base class validují, že předaný `mode` je jedna konkrétní hodnota (ne 0 ani combined flags), takže plugin se tím obvykle nemusí zabývat. Dialog odesílání nabízí vždy všechny providery; módy, které vybraný provider nepodporuje (podle `SupportedModes`), jsou v dialogu zakázané, takže do pluginu nedoteče mode, který nepodporuje.

### `WorkSuggestionPluginBase`

Pro pluginy s návrhy úkolů:

- `TestConnectionAsync(...)`
- `GetSuggestionsAsync(DateTime date, CancellationToken)` — vrať návrhy pro daný den
- `virtual bool SupportsSearch => false` — přepiš na `true`, pokud plugin zvládá textové vyhledávání
- `virtual SearchAsync(string query, CancellationToken)` — výchozí vrací failure; přepiš, pokud `SupportsSearch => true`

### `StatusIndicatorPluginBase`

Pro fyzické indikátory:

- `abstract bool IsDeviceAvailable { get; }` — kontrola přítomnosti hardware
- `abstract SetStateAsync(StatusIndicatorState state, CancellationToken)` — nastav LED/displej podle fáze

`StatusIndicatorState` je enum: `Idle`, `Work`, `ShortBreak`, `LongBreak`.

---

## Metadata

Každý plugin musí vystavit `PluginMetadata`:

```csharp
public override PluginMetadata Metadata => new()
{
    Id = "author.pluginname",             // unikátní; formát [a-z0-9.-] je konvence, nevaliduje se
    Name = "Display Name",                 // pro GUI
    Version = new Version(1, 2, 3),
    Author = "Tvé jméno nebo tým",
    Description = "Krátký popis v češtině.",
    Website = "https://github.com/user/repo",
    MinimumAppVersion = new Version(1, 0, 0),  // informativní, aplikace ji zatím nekontroluje
    IconName = "FileDocument",             // název z Material Icons; zatím jen u suggestion pluginů
    Tags = ["tag1", "tag2"],
};
```

**`Id` je kritický** — používá se pro persistenci konfigurace, enabled state, reference z UI. Jakmile plugin vydáš, **nezměň Id** — uživatelé by ztratili konfiguraci. Vhodná konvence: `{doména}.{funkce}`, např. `tempo.worklog`, `jira.suggestions`.

**`IconName`** je hint pro Avalonia UI. Zatím ho používají jen suggestion pluginy — ikona jejich skupiny v dialogu Návrhy (bez něj `LightbulbOutline`). Názvy ikon najdeš na [pictogrammers.com/library/mdi](https://pictogrammers.com/library/mdi/).

---

## Konfigurace

Plugin popisuje svou konfiguraci deklarativně přes `PluginConfigurationField`. Nastavení z toho vygeneruje formulář; validaci dělá `PluginBase` při inicializaci (viz [§ PluginBase](#pluginbase)).

### Typy polí

V Nastavení se zatím všechny typy kromě `Checkbox` zobrazují jako stejný jednořádkový textbox s popiskem (`Label`) a placeholderem (`Placeholder`). `Type` tak ovlivňuje hlavně ukládání:

| Typ | V Nastavení | Poznámka |
|-----|-------------|----------|
| `Text` | Textbox | Výchozí |
| `Password` | Textbox (**nemaskovaný**) | Hodnota se ukládá do secure storage |
| `Url` | Textbox | Formát se nekontroluje — použij `ValidationPattern` u povinného pole, nebo `OnValidateConfigurationAsync` |
| `Number` | Textbox | Totéž; hodnotu parsuj sám |
| `Email` | Textbox | Totéž |
| `MultilineText` | Textbox (jednořádkový) | |
| `Checkbox` | Checkbox | Ukládá se jako `"true"` / `"false"` |
| `Dropdown` | Textbox | `PluginConfigurationField` nemá jak definovat volby |

`Description` pole se v Nastavení zatím nezobrazuje. Nápovědu, kterou uživatel musí vidět, dej do `Label` nebo `Placeholder`.

### Příklad: konfigurace ve stylu Jira pluginu

Klíče jsou zjednodušené; skutečný Atlassian plugin používá `JiraBaseUrl`, `JiraEmail` a `JiraApiToken` (`JiraConfigFields.cs`).

```csharp
public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() =>
[
    new PluginConfigurationField
    {
        Key = "BaseUrl",
        Label = "Jira Base URL",
        Type = PluginConfigurationFieldType.Url,
        IsRequired = true,
        Placeholder = "https://vase-firma.atlassian.net",
        ValidationPattern = @"^https://.*",    // vyhodnotí se, protože pole je povinné
        ValidationMessage = "Zadej HTTPS URL.",
    },
    new PluginConfigurationField
    {
        Key = "Email",
        Label = "Login email",
        Type = PluginConfigurationFieldType.Email,
        IsRequired = true,
    },
    new PluginConfigurationField
    {
        Key = "ApiToken",
        Label = "API Token",
        Type = PluginConfigurationFieldType.Password,  // secure storage
        IsRequired = true,
        Placeholder = "Token z id.atlassian.com → Security → API tokens",
    },
    new PluginConfigurationField
    {
        Key = "JqlFilter",
        Label = "JQL filtr",
        Type = PluginConfigurationFieldType.MultilineText,
        IsRequired = false,
        DefaultValue = "assignee = currentUser() AND status != Done ORDER BY updated DESC",
    },
    new PluginConfigurationField
    {
        Key = "MaxResults",
        Label = "Maximum výsledků",
        Type = PluginConfigurationFieldType.Number,
        IsRequired = false,
        DefaultValue = "20",
        // ValidationPattern by se u nepovinného pole nevyhodnotil — formát hlídá OnValidateConfigurationAsync níže.
    },
];
```

### Načítání hodnot v pluginu

```csharp
protected override async Task<bool> OnInitializeAsync(
    IDictionary<string, string> configuration,
    CancellationToken cancellationToken)
{
    var baseUrl = GetRequiredConfigValue("BaseUrl");       // throws, pokud chybí
    var email = GetRequiredConfigValue("Email");
    var token = GetRequiredConfigValue("ApiToken");        // plaintext, už unwrapovaný
    var jql = GetConfigValue("JqlFilter") ?? DefaultJql;
    var maxResults = int.Parse(GetConfigValue("MaxResults") ?? "20");

    // …použij je k inicializaci HTTP klienta…
    return true;
}
```

> **Plugin nevidí secure storage.** Hodnoty typu `Password` jsou už rozbalené (`Unprotect`) na plaintext v `Configuration`. Plugin se o šifrování nestará.

### Vlastní validace

Pokud výchozí validace nestačí (typicky formát nepovinného pole), přepiš `OnValidateConfigurationAsync`. Volá se až po úspěšné výchozí validaci:

```csharp
protected override async Task<PluginValidationResult> OnValidateConfigurationAsync(
    IDictionary<string, string> config, CancellationToken ct)
{
    var baseResult = await base.OnValidateConfigurationAsync(config, ct);
    if (!baseResult.IsValid)
    {
        return baseResult;
    }

    var errors = new List<string>();

    if (config.TryGetValue("MaxResults", out var maxResults)
        && !string.IsNullOrWhiteSpace(maxResults)
        && (!int.TryParse(maxResults, out var parsed) || parsed <= 0))
    {
        errors.Add("Maximum výsledků musí být kladné celé číslo.");
    }

    return errors.Count == 0
        ? PluginValidationResult.Success()
        : PluginValidationResult.Failure(errors.ToArray());
}
```

---

## ITestablePlugin a test connection

`IWorklogUploadPlugin` a `IWorkSuggestionPlugin` dědí `ITestablePlugin`, který vyžaduje:

```csharp
Task<PluginResult<bool>> TestConnectionAsync(
    IProgress<string>? progress, CancellationToken ct);
```

Tato metoda:

- Je volaná **ručně** z UI (tlačítko **Otestovat připojení** v Nastavení → Pluginy). Plugin se před testem inicializuje s neuloženými hodnotami z formuláře a po testu se vrátí k uložené konfiguraci.
- Má **pravdivě ověřit**, že plugin se dokáže připojit k externímu systému s aktuální konfigurací — ne jen „plugin je nainstalovaný“.
- Pro OAuth pluginy obvykle **triggeruje device code flow** (pokud není platný cached token).
- Má **`IProgress<string>` pro hlášení stavu uživateli** — na co se čeká, co se děje. Bez toho uživatel u device code flow netuší, co se děje.

### Příklad: test connection s device code

```csharp
public override async Task<PluginResult<bool>> TestConnectionAsync(
    IProgress<string>? progress, CancellationToken ct)
{
    EnsureInitialized();
    try
    {
        progress?.Report("Získávám token…");

        // Zkusí nejdřív token z cache; device code flow spustí, jen když tam není.
        var token = await _tokenProvider!.AcquireTokenInteractiveAsync(progress, ct);
        if (token is null)
        {
            return PluginResult<bool>.Failure(
                "Získání tokenu selhalo.",
                PluginErrorCategory.Authentication);
        }

        progress?.Report("Testuji volání API…");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return PluginResult<bool>.Failure(
                $"API vrátilo {response.StatusCode}",
                PluginErrorCategory.Network);
        }

        progress?.Report("Připojení v pořádku.");
        return PluginResult<bool>.Success(true);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Sem doletí jen zrušení HTTP volání. Token provider zrušení ani chyby MSAL nevyhazuje —
        // zaloguje je a vrátí null (větev „Získání tokenu selhalo“ výš). Nastavení dnes předává
        // CancellationToken.None, takže uživatel test zrušit nemůže.
        return PluginResult<bool>.Failure("Test byl zrušen.", PluginErrorCategory.Internal);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Test connection selhal");
        return PluginResult<bool>.Failure(ex.Message, PluginErrorCategory.Internal);
    }
}
```

---

## Plugin result a kategorizace chyb

`PluginResult<T>` je Result pattern pro pluginy. Dědí negenerický `PluginResult`, který slouží pro operace bez návratové hodnoty:

```csharp
public class PluginResult
{
    public bool IsSuccess { get; init; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; init; }
    public PluginErrorCategory? ErrorCategory { get; init; }   // jen u failure

    public static PluginResult Success();
    public static PluginResult Failure(string error, PluginErrorCategory category = PluginErrorCategory.Internal);
}

public class PluginResult<T> : PluginResult
{
    public T? Value { get; init; }

    public static PluginResult<T> Success(T value);
    public static new PluginResult<T> Failure(string error, PluginErrorCategory category = PluginErrorCategory.Internal);
}
```

### `PluginErrorCategory`

| Kategorie | Kdy použít |
|-----------|------------|
| `Validation` | Nevalidní konfigurace, nevalidní vstupní data |
| `Network` | HTTP chyba, timeout, connection refused, retryable 5xx |
| `Authentication` | 401/403, expirovaný token, selhání OAuth |
| `NotFound` | 404, entita neexistuje |
| `Internal` | Ostatní / neurčené (výchozí) |

Aplikace zatím `ErrorCategory` nevyhodnocuje — v Nastavení i při odesílání zobrazí jen text z `Error`. Kategorie je i tak součástí kontraktu a testy referenčních pluginů ji ověřují, takže ji nastavuj správně a nespoléhej na default `Internal`.

---

## Autentizace: ITokenProviderFactory a device code flow

Pluginy pro Microsoft Entra ID (Office 365, Goran G3, vlastní M365/Azure AD integrace) **musí** používat `ITokenProviderFactory` injektovaný z DI. Nevolej MSAL přímo — factory už řeší token cache, device code flow a cross‑platform šifrování.

### Získání factory

```csharp
public class MyPlugin : WorkSuggestionPluginBase
{
    private readonly ITokenProviderFactory _tokenProviderFactory;
    private ITokenProvider? _tokenProvider;

    public MyPlugin(
        ILogger<MyPlugin> logger,
        ITokenProviderFactory tokenProviderFactory)
        : base(logger)
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
}
```

### Získání tokenu

```csharp
private async Task<string?> AcquireTokenAsync(IProgress<string>? progress, CancellationToken ct)
{
    // Silent nejdřív — cached token
    var token = await _tokenProvider!.AcquireTokenSilentAsync(ct);
    if (token is not null)
    {
        return token;
    }

    // Silent selhalo → device code flow
    progress?.Report("Nutné nové přihlášení přes Microsoft.");
    return await _tokenProvider.AcquireTokenInteractiveAsync(progress, ct);
}
```

Explicitní silent pokus v ukázce je jen pro názornost — `AcquireTokenInteractiveAsync` ho dělá sám. Samotné `AcquireTokenSilentAsync` volej tam, kde interaktivní přihlášení spustit nechceš (běžná volání API mimo test připojení).

`AcquireTokenInteractiveAsync` zavnitřně:

1. Zkusí `AcquireTokenSilentAsync`; když je token v cache, rovnou ho vrátí.
2. Zavolá MSAL `AcquireTokenWithDeviceCode`.
3. Jakmile MSAL dodá `user code` a `verification URL`, reportne je do `progress` jako `⏳ Open browser and enter code: {kód}` a na dalším řádku URL.
4. Zároveň se pokusí otevřít browser přes `Process.Start` s verification URL.
5. Čeká, dokud uživatel nepotvrdí přihlášení (nebo dokud není `ct` cancelled).
6. Vrátí access token. Při chybě nebo zrušení ji zaloguje a vrátí `null`.

### Entra registrace

Aby fungovalo device code flow, aplikační registrace v Entra (Azure Portal → App registrations) musí:

- Mít povolené **„Allow public client flows“** → **Yes**.
- Mít nakonfigurované **scopes** (delegated permissions), které plugin požaduje — a uděleny user nebo admin consentem.
- Nepoužívat client secret (public client).

---

## Dependency injection v pluginech

Plugin se instanciuje přes `ActivatorUtilities.CreateInstance` z plugin `ServiceProvider`u, který si `PluginManager` postaví z vlastní `ServiceCollection` (jeden pro všechny pluginy, oddělený od DI hlavní aplikace). **Konstruktor může brát libovolnou kombinaci** následujících služeb:

| Služba | Popis |
|--------|-------|
| `ILogger<T>` | Typed logger pro plugin třídu |
| `ILoggerFactory` | Když potřebuješ loggery pro podkomponenty |
| `IHttpClientFactory` | Preferuj tuto factory pro běžné HTTP požadavky — stará se o poolování `HttpMessageHandler`. Pro speciální scénáře (custom `DelegatingHandler`, vlastní transport, knihovny, které přijímají `HttpClient` konstruktorem) je v pořádku vytvořit `new HttpClient(handler, disposeHandler: false)` a spravovat jeho lifetime ručně — viz `GoranG3WorklogPlugin` pro ukázku. |
| `ITokenProviderFactory` | MSAL device code flow |

Plugin je vždy instancovaný přes `ActivatorUtilities.CreateInstance`, takže všechny parametry konstruktoru musí být resolvovatelné ze service provideru. Bezparametrický konstruktor technicky funguje (pokud plugin žádné služby nepotřebuje), ale obvykle je vhodnější používat explicitní DI konstruktor — budeš aspoň mít přístup k loggeru.

### Správné použití HttpClient

```csharp
public class MyPlugin : WorklogUploadPluginBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MyPlugin(
        ILogger<MyPlugin> logger,
        IHttpClientFactory httpClientFactory)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override async Task<PluginResult<bool>> UploadWorklogAsync(
        PluginWorklogEntry worklog, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _token);

        using var response = await client.PostAsJsonAsync(_endpoint, worklog, ct);
        // …
    }
}
```

`IHttpClientFactory` zajišťuje správné poolování a recyklaci `HttpMessageHandler`ů (jinak vznikají socket leaks).

---

## Balení a deployment

### Adresářová struktura

Výstup `dotnet publish` pluginu patří do vlastní podsložky v plugin adresáři — výchozí je `plugins/` vedle binárky hlavní aplikace, na macOS `~/Library/Application Support/WorkTracker/plugins/` (viz [§ Co je plugin](#co-je-plugin)). Release zipy pluginů obsahují přesně takovou podsložku:

```
<install-dir>/
├── WorkTracker.Avalonia.exe                 (single-file, obsahuje i WorkTracker.Plugin.Abstractions)
└── plugins/
    ├── WorkTracker.Plugin.FileDump/
    │   ├── WorkTracker.Plugin.FileDump.dll
    │   └── (tvé závislosti .dll, NuGety, …)
    └── WorkTracker.Plugin.Atlassian/
        ├── WorkTracker.Plugin.Atlassian.dll
        └── (Jira client knihovny atd.)
```

Každý plugin **ve vlastní podsložce**. `PluginLoader` projde plugin adresáře rekurzivně a hledá soubory `WorkTracker.Plugin.*.dll` (kromě `WorkTracker.Plugin.Abstractions.dll`) — jakmile narazí na soubor, který se *jmenuje* tímto vzorem, zkusí ho načíst.

### Jmenné konvence

- DLL **musí** začínat `WorkTracker.Plugin.` — jinak ji loader přeskočí.
- Jmenný prostor třídy není omezený, ale konvence je `WorkTracker.Plugin.{Name}`.
- Třída pluginu **nesmí být abstraktní** a musí implementovat `IPlugin` (v praxi přes některou z base class).

### Closed‑box closed‑world

Plugin nese **všechny své závislosti** vedle sebe (`CopyLocalLockFileAssemblies`). Při načítání se ale `PluginLoadContext` pro každou závislost nejdřív podívá do Default `AssemblyLoadContext`u: je‑li tam už načtená assembly **stejného jména**, plugin dostane tu hostitelovu. Sdílí se tak `WorkTracker.Plugin.Abstractions`, `Microsoft.Extensions.*`, ale třeba i `WorkTracker.Domain`, na který odkazují Atlassian a GoranG3. Lokální kopie se použije jen pro assembly, které hostitel nenačetl. Co je „načtené v hostiteli“, závisí na tom, co aplikace stihla načíst do okamžiku načtení pluginu — pevný seznam neexistuje.

Verze se při tom neporovnává, takže plugin **nemůže přebít** verzi závislosti, kterou má načtenou i hostitel — dostane hostitelovu. Vlastní verzi si plugin může přinést jen u závislostí, které hostitel nemá (HID knihovna, MCP klient, …).

---

## Testování pluginu

Plugin testy jsou samostatný projekt v `tests/`, například `tests/WorkTracker.Plugin.FileDump.Tests/`. Šablona `dotnet new xunit` se nehodí: vytvoří projekt pro xUnit v2 s verzemi přímo v `PackageReference`, zatímco repo používá xUnit v3 přes Microsoft.Testing.Platform (`global.json`) a Central Package Management. Zkopíruj radši csproj existujícího testovacího projektu (např. `tests/WorkTracker.Plugin.Luxafor.Tests`) a uprav referenci:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Moq" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\plugins\WorkTracker.Plugin.FileDump\WorkTracker.Plugin.FileDump.csproj" />
  </ItemGroup>

</Project>
```

Projekt přidej do solution (`dotnet sln WorkTracker.slnx add tests/WorkTracker.Plugin.FileDump.Tests`) a spusť `dotnet test --project tests/WorkTracker.Plugin.FileDump.Tests`.

### Příklad testu

```csharp
public class FileDumpWorklogPluginTests
{
    private readonly Mock<ILogger<FileDumpWorklogPlugin>> _loggerMock = new();
    private readonly FileDumpWorklogPlugin _sut;
    private readonly string _tempFile;

    public FileDumpWorklogPluginTests()
    {
        _sut = new FileDumpWorklogPlugin(_loggerMock.Object);
        _tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
    }

    [Fact]
    public async Task InitializeAsync_WithMissingOutputPath_ReturnsFailure()
    {
        var result = await _sut.InitializeAsync(
            new Dictionary<string, string>(),
            CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UploadWorklogAsync_AppendsJsonLineToFile()
    {
        await _sut.InitializeAsync(
            new Dictionary<string, string> { ["OutputPath"] = _tempFile },
            CancellationToken.None);

        var worklog = new PluginWorklogEntry
        {
            TicketId = "PROJ-123",
            StartTime = new DateTime(2026, 4, 9, 9, 0, 0),
            EndTime = new DateTime(2026, 4, 9, 10, 0, 0),
            DurationMinutes = 60,
        };

        var result = await _sut.UploadWorklogAsync(worklog, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(_tempFile);
        content.Should().Contain("PROJ-123");
    }
}
```

### Co testovat

- **Validace konfigurace** — scénáře s chybějícími nebo neplatnými poli.
- **Happy path upload/suggestions** — s mock HTTP nebo in‑memory úložištěm.
- **Chybové stavy** — 401, 500, síťové chyby. `HttpMessageHandler` mocky jsou velmi efektivní pro to.
- **Kategorizace chyb** — že plugin vrací správný `PluginErrorCategory` pro různé HTTP kódy.
- **Retry logika** (pokud máš) — že retry skutečně retryne a po limitu se vzdá.

---

## Referenční pluginy

Nejjistější zdroj inspirace jsou **existující pluginy v adresáři `plugins/`**. Každý ukazuje jinou část API:

### `WorkTracker.Plugin.Atlassian`

- **Dvě třídy v jednom projektu**: `TempoWorklogPlugin` (upload) a `JiraSuggestionsPlugin` (suggestions).
- **Sdílený `JiraClient`** mezi oběma třídami — ukazuje, jak v jednom plugin projektu sdílet kód mezi víc plugin instancemi.
- **Issue key caching** (`ConcurrentDictionary` s TTL 1 hodina) pro Tempo ID lookupy.
- **Retry logika** v Tempo uploadu: až 2 opakování (3 pokusy celkem) s backoffem 2 s a 4 s na 408/429/500/502/503/504.
- **`SupportsSearch => true`** v Jira suggestions, včetně JQL template se `{query}` placeholderem.

Dokumentace: [plugins/atlassian.md](plugins/atlassian.md).

### `WorkTracker.Plugin.Office365Calendar`

- **MSAL device code flow** přes `ITokenProviderFactory`.
- **Microsoft Graph API** — `TestConnectionAsync` ověřuje připojení a identitu přes `GET /me`; samotné načítání eventů v `GetSuggestionsAsync` pak volá `GET /me/calendarView`.
- **Filtrace all‑day eventů** konfigurovatelná přes checkbox.
- Ukazuje, jak propagovat `IProgress<string>` z `TestConnectionAsync` až do MSAL callbacku.

Dokumentace: [plugins/office365-calendar.md](plugins/office365-calendar.md).

### `WorkTracker.Plugin.GoranG3`

- **Entra ID autentizace** + MCP client.
- **`TokenInjectingHandler`** — vlastní `DelegatingHandler` pro HttpClient, který do requestů vkládá Bearer token přes `AcquireTokenSilentAsync`. Interaktivní získání tokenu uvnitř handleru **neprobíhá** — pokud silent auth selže, handler vyhodí výjimku a uživatel musí spustit **Test connection** v Settings, kde se interaktivní device code flow rozběhne.
- **`ITestablePlugin`** implementace, která po připojení k MCP serveru listuje dostupné tools a kontroluje přítomnost očekávaného toolu (`create_my_timesheet_item`).
- Ukazuje, jak plugin může komunikovat s nestandardním protokolem (MCP), ne jen REST.

Dokumentace: [plugins/goran-g3.md](plugins/goran-g3.md).

### `WorkTracker.Plugin.Luxafor`

- **`IStatusIndicatorPlugin`** (ne worklog).
- **Komunikace se zařízením** přes knihovnu **`DotLuxafor`** (lokální NuGet feed `packages/`), která poskytuje `ILuxaforDeviceManager` / `ILuxaforDevice` API s metodami jako `SetColorAsync` a `TurnOffAsync`.
- **Lazy device open** — `OnInitializeAsync` jen naparsuje barvy z konfigurace, zařízení se otevírá až při prvním `SetStateAsync` nebo testu připojení. Výjimka: se zapnutým `turn_off_on_startup` pošle první inicializace `Idle`, takže zařízení otevře hned.
- **`ITestablePlugin`** i pro status indicator plugin — `StatusIndicatorPluginBase` ho nevyžaduje, ale plugin si ho přidá sám, takže se v Nastavení objeví **Otestovat připojení**, které rozsvítí LED a při neúspěchu řekne důvod (chybí udev pravidlo, zařízení drží jiná aplikace, …).
- **Reopen po odpojení** — `IsConnected` u HID zařízení znamená jen „handle jsme nezavřeli", takže plugin po `LuxaforDeviceDisconnectedException` zařízení jednou znovu otevře (přes `ex.Descriptor`, s fallbackem na `Open()`) a příkaz zopakuje. Užitečný vzor pro každý plugin ovládající hardware.
- **Konfigurovatelné barvy per Pomodoro fázi** — hex color pole s deklarovaným `ValidationPattern` `^#[0-9A-Fa-f]{6}$`. Pole nejsou povinná, takže `PluginBase` regex nevyhodnotí; neplatná hodnota tiše spadne na výchozí barvu (`ParseColor`).
- **Thread‑safe** s `SemaphoreSlim` — hlídá stav pluginu (otevírání a zavírání zařízení, poslední stav), aby se souběžná volání nepraly o handle.

Dokumentace: [plugins/luxafor.md](plugins/luxafor.md).

---

## Dále

- Rozšíření konfigurace o custom validaci → [§ Vlastní validace](#vlastní-validace)
- Jak se plugin účastní transakcí v hlavní aplikaci → **nikdy** nepřistupuje k `DbContext` / `IUnitOfWork` — plugin je stateless vůči databázi worktrackeru.
- Jak debugovat plugin z IDE → nastav Avalonia jako startup. Debug build načítá pluginy z repa přímo z `plugins/*/bin/Debug/net10.0` (`appsettings.Development.json`), takže stačí plugin buildnout — nový plugin do toho seznamu přidej (viz [§ Build a nasazení](#3-build-a-nasazení)). Plugin má `.pdb` a breakpointy fungují.
