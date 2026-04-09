# Vývoj pluginů

Průvodce pro autory pluginů. Pokud tě zajímá **jak plugin systém funguje interně** (AssemblyLoadContext, DI scope, lifecycle), viz [architecture.md § Plugin systém](architecture.md#plugin-systém).

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
- **Mohl být unloadnutý** bez restartu aplikace.
- **Neshazoval** hlavní aplikaci svými chybami.

Pluginy jsou umístěné v adresáři `plugins/` vedle binárky hlavní aplikace a automaticky se načtou při startu.

---

## Tři typy pluginů

| Typ | Rozhraní | Base class | Kdy použít |
|-----|----------|------------|------------|
| **Worklog Upload** | `IWorklogUploadPlugin` | `WorklogUploadPluginBase` | Chceš odesílat / číst worklogy z externího systému. |
| **Work Suggestion** | `IWorkSuggestionPlugin` | `WorkSuggestionPluginBase` | Chceš aplikaci dodat seznam úkolů k zobrazení v Suggestions dialogu. |
| **Status Indicator** | `IStatusIndicatorPlugin` | `StatusIndicatorPluginBase` | Chceš reagovat na Pomodoro fáze ovládáním externího zařízení. |

Jeden plugin může implementovat **víc** rozhraní (například `Plugin.Atlassian` obsahuje samostatné třídy pro Tempo upload a pro Jira suggestions, které sdílí `JiraClient`).

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
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\WorkTracker.Plugin.Abstractions\WorkTracker.Plugin.Abstractions.csproj">
      <!-- Plugin nenese Abstractions.dll — je sdílená s hlavní aplikací -->
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

> **Důležité:** `WorkTracker.Plugin.Abstractions.dll` se **nesmí** kopírovat vedle pluginu. Jinak by se načetla do jiného `AssemblyLoadContext` než hlavní aplikace a `IPlugin` z pluginu by nebyl přiřaditelný k `IPlugin` v aplikaci. `Private=false` + `ExcludeAssets=runtime` tomu zabrání.

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
        Description = "Zapisuje worklogy do JSON souboru na disku.",
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
            Placeholder = @"C:\temp\worklogs.json",
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

            // Zkusíme zapsat prázdný soubor
            await File.WriteAllTextAsync(path, "[]", ct);
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

```bash
dotnet publish plugins/WorkTracker.Plugin.FileDump -c Release \
  -o src/WorkTracker.Avalonia/bin/Debug/net10.0/plugins/FileDump
```

Spusť Avalonia aplikaci — plugin se objeví v **Settings → Plugins** jako „File Dump“. Zadej cestu k souboru, stiskni **Test connection**, povolí a klikni na **Send today**.

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
        CancellationToken cancellationToken) => /* default: vrací Success(); hook pro dodatečná pravidla nad rámec základní IsRequired + regex validace, kterou dělá ValidateConfigurationAsync před zavoláním tohoto hooku */;
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    // Helpery:
    protected string? GetConfigValue(string key);
    protected string GetRequiredConfigValue(string key);  // throws if missing
    protected void EnsureInitialized();  // throws if not
}
```

> `OnInitializeAsync` dostává `configuration` dict jako parametr. V rámci hooků můžeš použít buď tento parametr, nebo `base.Configuration` (jsou identické — base class si dict uloží před voláním hooku). Používej `GetRequiredConfigValue` / `GetConfigValue`, které čtou z `Configuration` a dávají konzistentní error reporting.

**Výchozí validace** kontroluje:
- `IsRequired = true` → hodnota je přítomná a neprázdná.
- `ValidationPattern` (regex) → hodnota odpovídá vzoru; chybová hláška z `ValidationMessage`.

### `WorklogUploadPluginBase`

Dědí `PluginBase` a implementuje `IWorklogUploadPlugin`. Definuje abstraktní metody:

- `TestConnectionAsync(IProgress<string>?, CancellationToken)`
- `UploadWorklogAsync(PluginWorklogEntry, CancellationToken)`
- `GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken)`
- `WorklogExistsAsync(PluginWorklogEntry, CancellationToken)`

Virtual metoda `UploadWorklogsAsync` má **výchozí implementaci**, která prochází záznamy jeden po druhém, volá `UploadWorklogAsync` a sbírá chyby do `WorklogSubmissionResult`. Stačí přepsat, jen pokud chceš hromadné API (např. batch endpoint).

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
    Id = "author.pluginname",             // unique, [a-z0-9.-]
    Name = "Display Name",                 // pro GUI
    Version = new Version(1, 2, 3),
    Author = "Tvé jméno nebo tým",
    Description = "Krátký popis v češtině.",
    Website = "https://github.com/user/repo",
    MinimumAppVersion = new Version(1, 0, 0),  // minimální verze WorkTrackeru
    IconName = "FileDocument",             // název z Material Icons (Avalonia)
    Tags = ["tag1", "tag2"],
};
```

**`Id` je kritický** — používá se pro persistenci konfigurace, enabled state, reference z UI. Jakmile plugin vydáš, **nezměň Id** — uživatelé by ztratili konfiguraci. Vhodná konvence: `{doména}.{funkce}`, např. `tempo.worklog`, `jira.suggestions`.

**`IconName`** je hint pro Avalonia UI. Materiál ikony najdeš na [pictogrammers.com/library/mdi](https://pictogrammers.com/library/mdi/). WPF UI tento hint ignoruje (používá Material Design palety).

---

## Konfigurace

Plugin popisuje svou konfiguraci deklarativně přes `PluginConfigurationField`. WorkTracker UI z toho vygeneruje formulář a zajistí validaci.

### Typy polí

| Typ | Vzhled | Poznámka |
|-----|--------|----------|
| `Text` | Textbox | Default |
| `Password` | Maskovaný textbox; hodnota **automaticky uložená do secure storage** |
| `Url` | Textbox s URL validací |
| `Number` | Numerický vstup |
| `Email` | Textbox s email validací |
| `MultilineText` | Textarea | Pro JQL, popisy |
| `Checkbox` | Checkbox | Ukládá se jako `"true"` / `"false"` |
| `Dropdown` | Combo box | Potřebuje další konfiguraci (ne všechno je v šabloně zdokumentované) |

### Příklad: konfigurace Atlassian Jira

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
        Description = "Vygeneruj na id.atlassian.com/manage-profile/security/api-tokens",
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
        ValidationPattern = @"^\d+$",
        ValidationMessage = "Musí být celé číslo.",
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

Pokud výchozí regex/required validace nestačí, přepiš `OnValidateConfigurationAsync`:

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

    if (config.TryGetValue("BaseUrl", out var url) && !url.StartsWith("https://"))
    {
        errors.Add("Base URL musí začínat https://");
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

- Je volaná **ručně** z UI (tlačítko **Test connection** v Settings).
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

        var token = await _tokenProvider!.AcquireTokenSilentAsync(ct);
        if (token is null)
        {
            progress?.Report("Silent token neexistuje, spouštím přihlášení.");
            token = await _tokenProvider.AcquireTokenInteractiveAsync(progress, ct);
        }

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
    catch (OperationCanceledException)
    {
        return PluginResult<bool>.Failure("Přerušeno uživatelem.", PluginErrorCategory.Internal);
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

`PluginResult<T>` je Result pattern pro pluginy:

```csharp
public sealed class PluginResult<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public PluginErrorCategory? ErrorCategory { get; }
    public T? Value { get; }

    public static PluginResult<T> Success(T value);
    public static PluginResult<T> Failure(string error, PluginErrorCategory category = PluginErrorCategory.Internal);
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

Aplikace používá kategorie pro:

- **UI** — zobrazí ikonu a doporučení podle kategorie (u `Authentication` nabídne „Přihlásit znovu“).
- **Retry logika** — u `Network` nabídne retry; u `Validation` ne.
- **Logging** — kategorie je v metadatech logu, takže se dá snadno filtrovat.

Buď explicitní, nespoléhej na default `Internal`.

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

`AcquireTokenInteractiveAsync` zavnitřně:

1. Zavolá MSAL `AcquireTokenWithDeviceCode`.
2. Jakmile MSAL dodá `user code` a `verification URL`, reportne je do `progress` ve formě lidsky čitelného textu.
3. Zároveň se pokusí otevřít browser přes `Process.Start` s verification URL.
4. Čeká, dokud uživatel nepotvrdí přihlášení (nebo dokud není `ct` cancelled).
5. Vrátí access token.

### Entra registrace

Aby fungovalo device code flow, aplikační registrace v Entra (Azure Portal → App registrations) musí:

- Mít povolené **„Allow public client flows“** → **Yes**.
- Mít nakonfigurované **scopes** (delegated permissions), které plugin požaduje — a uděleny user nebo admin consentem.
- Nepoužívat client secret (public client).

---

## Dependency injection v pluginech

Plugin se instanciuje přes `ActivatorUtilities.CreateInstance` ze scoped `ServiceCollection` spravovaného `PluginManager`em. **Konstruktor může brát libovolnou kombinaci** následujících služeb:

| Služba | Popis |
|--------|-------|
| `ILogger<T>` | Typed logger pro plugin třídu |
| `ILoggerFactory` | Když potřebuješ loggery pro podkomponenty |
| `IHttpClientFactory` | **Vždy** použij tuto factory, nikdy nenew HttpClient přímo |
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

Po `dotnet publish` projde plugin do složky `plugins/` vedle binárky hlavní aplikace:

```
<install-dir>/
├── WorkTracker.Avalonia.exe
├── WorkTracker.Plugin.Abstractions.dll     (sdílené)
└── plugins/
    ├── FileDump/
    │   ├── WorkTracker.Plugin.FileDump.dll
    │   └── (tvé závislosti .dll, NUGETy, …)
    └── Atlassian/
        ├── WorkTracker.Plugin.Atlassian.dll
        └── (Jira client knihovny atd.)
```

Každý plugin **ve vlastní podsložce**. `PluginLoader` projde rekurzivně a hledá soubory `WorkTracker.Plugin.*.dll` — jakmile narazí na soubor, který se *jmenuje* tímto vzorem, zkusí ho načíst.

### Jmenné konvence

- DLL **musí** začínat `WorkTracker.Plugin.` — jinak ji loader přeskočí.
- Jmenný prostor třídy není omezený, ale konvence je `WorkTracker.Plugin.{Name}`.
- Třída pluginu **nesmí být abstraktní**.

### Closed‑box closed‑world

Plugin nese **všechny své závislosti** vedle sebe — kromě `WorkTracker.Plugin.Abstractions.dll` a BCL (`System.*`, `Microsoft.Extensions.*`). Shared assembly zajistí Default `AssemblyLoadContext`; vše ostatní plugin vlastní.

Pokud plugin potřebuje jinou verzi závislosti, než hlavní aplikace, **musí** ji mít vedle sebe. Isolation kontextu to umožní — `PluginLoadContext` upřednostňuje lokální verzi.

---

## Testování pluginu

Plugin testy jsou samostatný projekt v `tests/`, například `tests/WorkTracker.Plugin.FileDump.Tests/`.

```bash
dotnet new xunit -n WorkTracker.Plugin.FileDump.Tests -o tests/WorkTracker.Plugin.FileDump.Tests
cd tests/WorkTracker.Plugin.FileDump.Tests
dotnet add reference ../../plugins/WorkTracker.Plugin.FileDump/WorkTracker.Plugin.FileDump.csproj
dotnet add reference ../../src/WorkTracker.Plugin.Abstractions/WorkTracker.Plugin.Abstractions.csproj
dotnet add package Moq
dotnet add package FluentAssertions
```

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
- **Retry logika** s exponenciálním backoffem na 408/429/500–504.
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
- **Komunikace se zařízením** přes knihovnu **`DotLuxafor`** (NuGet package), která poskytuje `ILuxaforDeviceManager` / `ILuxaforDevice` API s metodami jako `SetColorAsync` a `TurnOffAsync`.
- **Lazy device open** — `OnInitializeAsync` jen naparsuje barvy z konfigurace, zařízení se otevírá až při prvním `SetStateAsync`.
- **Konfigurovatelné barvy per Pomodoro fázi** — hex color pole s regex validací `^#[0-9A-Fa-f]{6}$`.
- **Thread‑safe** s `SemaphoreSlim` — operace se zařízením nesmí běžet paralelně.

Dokumentace: [plugins/luxafor.md](plugins/luxafor.md).

---

## Dále

- Rozšíření konfigurace o custom validaci → [§ Vlastní validace](#vlastní-validace)
- Jak se plugin účastní transakcí v hlavní aplikaci → **nikdy** nepřistupuje k `DbContext` / `IUnitOfWork` — plugin je stateless vůči databázi worktrackeru.
- Jak debugovat plugin z IDE → nastav Avalonia jako startup, v Debug buildnutý plugin publikuj do `bin/Debug/.../plugins/`, plugin má `.pdb` a breakpointy fungují.
