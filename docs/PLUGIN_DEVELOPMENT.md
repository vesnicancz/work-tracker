# WorkTracker - Plugin Development Guide

**Complete guide to creating WorkTracker plugins**

Version: 1.4
Last Updated: April 2026

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Plugin Architecture](#2-plugin-architecture)
3. [Quick Start](#3-quick-start)
4. [Plugin Abstractions](#4-plugin-abstractions)
5. [Configuration](#5-configuration)
6. [Testing](#6-testing)
7. [Deployment](#7-deployment)
8. [Reference Plugins](#8-reference-plugins)

---

## 1. Introduction

### 1.1 What are Plugins?

WorkTracker plugins are **standalone DLL assemblies** that extend WorkTracker's functionality without modifying the core application. The primary use case is integrating with external time tracking systems (Jira Tempo, Azure DevOps, Clockify, etc.).

### 1.2 Plugin Capabilities

Plugins can:
- ✅ Upload worklogs to external systems
- ✅ Validate worklog data
- ✅ Test connections to external APIs
- ✅ Retrieve existing worklogs
- ✅ Check for duplicate entries
- ✅ Define custom configuration fields
- ✅ Control status indicator devices (LED lights, etc.)
- ✅ Navrhovat pracovni zaznamy z externich zdroju (kalendar, issue tracker)

Plugins cannot:
- ❌ Modify WorkTracker's core behavior
- ❌ Access other plugins directly
- ❌ Directly manipulate the database
- ❌ Override UI components

### 1.3 Plugin Types

WorkTracker supports tri typy pluginu:

1. **IWorklogUploadPlugin** - Upload work logu do externich systemu (napr. Tempo, Azure DevOps)
2. **IStatusIndicatorPlugin** - Ovladani fyzickych stavovych indikatoru (napr. Luxafor LED)
3. **IWorkSuggestionPlugin** - Navrhy pracovnich zaznamu z externich zdroju (napr. Jira issues, Office 365 kalendar)

---

## 2. Plugin Architecture

### 2.1 Plugin Lifecycle

```
┌─────────────────────────────────────────────────┐
│  1. Discovery (PluginLoader)                     │
│     - Scans configured plugin directories       │
│     - Finds DLLs matching WorkTracker.Plugin.*  │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  2. Loading (PluginLoader)                       │
│     - Creates isolated AssemblyLoadContext      │
│     - Loads plugin assembly                     │
│     - Instantiates via ActivatorUtilities (DI)  │
│     - Constructor receives ILogger<T>, etc.     │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  3. Registration (PluginManager)                 │
│     - Registers plugin by ID                    │
│     - Manages enabled/disabled state            │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  4. Initialization                               │
│     - Calls InitializeAsync()                   │
│     - Passes configuration from settings        │
│     - Plugin validates config                   │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  5. Usage                                        │
│     - Application calls plugin methods          │
│     - Plugin performs work (upload, etc.)       │
│     - Returns PluginResult<T>                   │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  6. Shutdown                                     │
│     - Calls ShutdownAsync()                     │
│       → OnShutdownAsync() for plugin logic      │
│       → DisposeAsync() for resource cleanup     │
│     - AssemblyLoadContext unloaded              │
└─────────────────────────────────────────────────┘
```

### 2.2 Constructor Injection (DI)

Plugins are instantiated via `ActivatorUtilities.CreateInstance` from a plugin-scoped `ServiceCollection`. Plugin constructors can accept any of these registered services:

| Service | Description |
|---------|-------------|
| `ILogger<T>` | Structured logging (via `ILoggerFactory`) |
| `IHttpClientFactory` | HTTP client creation (preferred over raw `HttpClient`) |
| `ITokenProviderFactory` | MSAL-based Azure AD token acquisition |

Example constructor:

```csharp
public class MyPlugin(ILogger<MyPlugin> logger, IHttpClientFactory httpClientFactory)
    : WorklogUploadPluginBase(logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
}
```

There is **no parameterless constructor** requirement. Do **not** use `Activator.CreateInstance`. The `ILogger` is passed to the base class via primary constructor — there is no `SetLogger()` method.

### 2.3 Assembly Isolation

Plugins are loaded in **isolated AssemblyLoadContexts**, which means:

✅ **Benefits:**
- Plugin dependencies don't conflict with host app
- Plugins can use different versions of same library
- Plugins can be unloaded at runtime

⚠️ **Limitations:**
- Shared types must be in abstractions assembly
- Can't directly pass plugin-specific types to host

### 2.4 Plugin Discovery

`PluginLoader` handles discovery and file loading. `PluginManager` handles registration, lifecycle, filtering, and enabled state.

The default plugin directory is relative to the application executable:

```
<AppContext.BaseDirectory>/plugins/
├── WorkTracker.Plugin.Atlassian.dll
├── WorkTracker.Plugin.Office365Calendar.dll
├── WorkTracker.Plugin.Luxafor.dll
├── WorkTracker.Plugin.GoranG3.dll
└── MyPlugin.dll
```

Additional plugin directories can be configured via `Plugins:Directories` in `appsettings.json`:

```json
{
  "Plugins": {
    "Directories": [ "plugins", "C:\\MyCustomPlugins" ]
  }
}
```

Relative paths are resolved against `AppContext.BaseDirectory`. See `WorkTrackerPaths.DefaultPluginsPath` for the default.

During publish an empty `plugins/` scan directory is created under `<AppContext.BaseDirectory>`. Plugin DLLs are distributed as separate artifacts and must be copied or extracted into one of the configured plugin directories (typically `<AppContext.BaseDirectory>/plugins/`) for the application to discover them. All plugins are loaded the same way via directory scan — there is no distinction between shipped and third-party plugins at runtime.

---

## 3. Quick Start

### 3.1 Create Plugin Project

```bash
# Create class library
dotnet new classlib -n WorkTracker.Plugin.MySystem

# Add reference to abstractions
cd WorkTracker.Plugin.MySystem
dotnet add reference ../WorkTracker.Plugin.Abstractions/WorkTracker.Plugin.Abstractions.csproj

# Add NuGet packages you need
dotnet add package Newtonsoft.Json
dotnet add package System.Net.Http
```

### 3.2 Implement Plugin

Choose a base class based on your plugin type:

- **`WorklogUploadPluginBase`** for worklog upload — see `plugins/WorkTracker.Plugin.Atlassian/TempoWorklogPlugin.cs`
- **`WorkSuggestionPluginBase`** for work suggestions — see `plugins/WorkTracker.Plugin.Atlassian/JiraSuggestionsPlugin.cs`
- **`StatusIndicatorPluginBase`** for status indicators — see `plugins/WorkTracker.Plugin.Luxafor/`

All base classes use primary constructors accepting `ILogger`. Your plugin constructor accepts DI services and forwards `ILogger<T>` to the base:

```csharp
public class MyPlugin(ILogger<MyPlugin> logger, IHttpClientFactory httpClientFactory)
    : WorklogUploadPluginBase(logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public override PluginMetadata Metadata => new()
    {
        Id = "my-plugin",
        Name = "My Plugin",
        Version = new Version(1, 0, 0),
        Author = "Author"
    };

    public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => [ /* ... */ ];

    protected override Task<bool> OnInitializeAsync(
        IDictionary<string, string> configuration, CancellationToken cancellationToken)
    {
        // Setup logic — create clients, validate credentials
        return Task.FromResult(true);
    }

    // Implement abstract methods from the chosen base class...
}
```

Every plugin must provide:
1. `Metadata` — plugin ID, name, version, author
2. `GetConfigurationFields()` — configuration fields the user fills in Settings UI
3. `OnInitializeAsync()` — setup logic (create HTTP clients, validate credentials)
4. The abstract methods from the chosen base class (e.g. `UploadWorklogAsync`, `GetSuggestionsAsync`)
5. `TestConnectionAsync(IProgress<string>?, CancellationToken)` — connection test

### 3.3 Build and Deploy

```bash
dotnet build -c Release
# Copy DLL to plugin directory (see Section 7 for details)
```

---

## 4. Plugin Abstractions

### 4.1 IPlugin Interface

**Base interface for all plugins. Extends `IAsyncDisposable` for resource cleanup:**

```csharp
public interface IPlugin : IAsyncDisposable
{
    /// <summary>
    /// Plugin metadata (ID, name, version, etc.)
    /// </summary>
    PluginMetadata Metadata { get; }

    /// <summary>
    /// Get configuration fields for this plugin (e.g. API keys, colors, URLs)
    /// </summary>
    IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

    /// <summary>
    /// Initialize plugin with configuration
    /// </summary>
    Task<bool> InitializeAsync(IDictionary<string, string>? configuration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate configuration
    /// </summary>
    Task<PluginValidationResult> ValidateConfigurationAsync(
        IDictionary<string, string> configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shutdown and cleanup resources. Calls OnShutdownAsync() then DisposeAsync().
    /// </summary>
    Task ShutdownAsync();
}
```

`PluginBase` provides a default `DisposeAsync()` (no-op). Plugins override `DisposeAsync()` for resource cleanup — do **not** implement `IAsyncDisposable` directly on your plugin class, it is inherited from `PluginBase`.

### 4.2 ITestablePlugin Interface

**Spolecny interface pro pluginy s testovanim pripojeni. Dedi z nej IWorklogUploadPlugin i IWorkSuggestionPlugin:**

```csharp
public interface ITestablePlugin : IPlugin
{
    /// <summary>
    /// Tests connection with optional progress reporting (e.g., for OAuth flows).
    /// </summary>
    Task<PluginResult<bool>> TestConnectionAsync(
        IProgress<string>? progress, CancellationToken cancellationToken);
}
```

Jedina metoda s `IProgress<string>?` parametrem. Pluginy ktere nepotrebuji progress reporting jednodusse ignoruji parametr `progress`. Pluginy vyzadujici interakci (napr. Office 365 Calendar s device code flow) reportuji stav prihlaseni do Settings UI pres `progress`.

### 4.3 IWorklogUploadPlugin Interface

**Specializovany interface pro upload worklogs:**

```csharp
public interface IWorklogUploadPlugin : ITestablePlugin
{
    /// <summary>
    /// Upload single worklog
    /// </summary>
    Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);

    /// <summary>
    /// Upload multiple worklogs
    /// </summary>
    Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(
        IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken);

    /// <summary>
    /// Get worklogs for date range
    /// </summary>
    Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

    /// <summary>
    /// Check if worklog already exists
    /// </summary>
    Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);
}
```

### 4.4 IStatusIndicatorPlugin Interface

**Specializovany interface pro fyzicke stavove indikatory (LED svetla, atd.):**

```csharp
public interface IStatusIndicatorPlugin : IPlugin
{
    /// <summary>
    /// Whether the physical device is connected and ready
    /// </summary>
    bool IsDeviceAvailable { get; }

    /// <summary>
    /// Update device to reflect the given Pomodoro phase
    /// </summary>
    Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken);
}

public enum StatusIndicatorState { Idle, Work, ShortBreak, LongBreak }
```

The Pomodoro timer calls `SetStateAsync()` on every phase transition. The plugin maps states to device-specific actions (e.g. LED colors).

See `plugins/WorkTracker.Plugin.Luxafor/` for a complete example using the `Luxafor.HidSharp` library.

### 4.5 IWorkSuggestionPlugin Interface

**Specializovany interface pro pluginy navrhujici pracovni zaznamy z externich zdroju (kalendar, issue tracker):**

```csharp
public interface IWorkSuggestionPlugin : ITestablePlugin
{
    /// <summary>
    /// Gets work suggestions for a specific date
    /// </summary>
    Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(
        DateTime date, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this plugin supports text-based search (e.g., Jira issue search).
    /// When false, only date-based GetSuggestionsAsync is used.
    /// </summary>
    bool SupportsSearch { get; }

    /// <summary>
    /// Searches for suggestions matching a text query.
    /// Only called when SupportsSearch is true.
    /// </summary>
    Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(
        string query, CancellationToken cancellationToken);
}
```

- `GetSuggestionsAsync(date)` - hlavni metoda, vraci navrhy pro dany den (napr. udalosti z kalendare, issues prirazene uzivateli)
- `SupportsSearch` - indikuje, zda plugin podporuje textove vyhledavani (napr. Jira ano, kalendar ne)
- `SearchAsync(query)` - volana pouze pokud `SupportsSearch == true`

### 4.6 WorkSuggestion DTO

```csharp
public class WorkSuggestion
{
    public required string Title { get; init; }       // Nazev navrhu (napr. predmet schuzky, summary issue)
    public string? TicketId { get; init; }            // Ticket/Issue ID pokud existuje (napr. "PROJ-123")
    public string? Description { get; init; }         // Volitelny delsi popis
    public DateTime? StartTime { get; init; }         // Navrhovany zacatek (napr. zacatek udalosti v kalendari)
    public DateTime? EndTime { get; init; }           // Navrhovany konec
    public required string Source { get; init; }      // Nazev zdrojoveho pluginu (napr. "Jira", "Office 365 Calendar")
    public required string SourceId { get; init; }    // Unikatni ID ve zdroji (pro deduplikaci)
    public string? SourceUrl { get; init; }           // URL na zdrojovy zaznam
}
```

### 4.7 WorkSuggestionPluginBase

**Abstraktni base class pro suggestion pluginy. Dedi z `PluginBase` (sdilena konfigurace, validace, lifecycle). Pouziva primary constructor s `ILogger`:**

```csharp
public abstract class WorkSuggestionPluginBase(ILogger logger) : PluginBase(logger), IWorkSuggestionPlugin
{
    // Povinne k implementaci
    public abstract Task<PluginResult<bool>> TestConnectionAsync(
        IProgress<string>? progress, CancellationToken cancellationToken);
    public abstract Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(
        DateTime date, CancellationToken cancellationToken);

    // Volitelne - vychozi je false / not supported
    public virtual bool SupportsSearch => false;
    public virtual Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(
        string query, CancellationToken cancellationToken);
}
```

Minimalni implementace vyzaduje pouze `Metadata`, `GetConfigurationFields()`, `TestConnectionAsync()` a `GetSuggestionsAsync()`. Search je opt-in.

### 4.8 WorklogUploadPluginBase

**Abstract base class with helper methods. Pouziva primary constructor s `ILogger`:**

```csharp
public abstract class WorklogUploadPluginBase(ILogger logger) : PluginBase(logger), IWorklogUploadPlugin
{
    // Inherited from PluginBase:
    //   protected ILogger Logger { get; }              (non-nullable)
    //   protected IDictionary<string, string> Configuration { get; }
    //   protected string? GetConfigValue(string key);
    //   protected string GetRequiredConfigValue(string key);
    //   protected void EnsureInitialized();

    // Abstract members to implement
    public abstract PluginMetadata Metadata { get; }
    public abstract IReadOnlyList<PluginConfigurationField> GetConfigurationFields();
    public abstract Task<PluginResult<bool>> TestConnectionAsync(
        IProgress<string>? progress, CancellationToken cancellationToken);
    public abstract Task<PluginResult<bool>> UploadWorklogAsync(
        PluginWorklogEntry worklog, CancellationToken cancellationToken);
    public abstract Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken);
    public abstract Task<PluginResult<bool>> WorklogExistsAsync(
        PluginWorklogEntry worklog, CancellationToken cancellationToken);

    // Default batch upload (calls UploadWorklogAsync for each entry)
    public virtual Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(
        IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken);
}
```

### 4.9 StatusIndicatorPluginBase

**Abstract base class pro status indicator pluginy. Pouziva primary constructor s `ILogger`:**

```csharp
public abstract class StatusIndicatorPluginBase(ILogger logger) : PluginBase(logger), IStatusIndicatorPlugin
{
    public abstract bool IsDeviceAvailable { get; }
    public abstract Task SetStateAsync(StatusIndicatorState state, CancellationToken cancellationToken);
}
```

### 4.10 Core Types

#### PluginMetadata

```csharp
public class PluginMetadata
{
    public required string Id { get; init; }              // Unique identifier
    public required string Name { get; init; }            // Display name
    public required Version Version { get; init; }        // System.Version
    public required string Author { get; init; }          // Author name
    public string? Description { get; init; }             // Short description
    public string? Website { get; init; }                 // Website or repository URL
    public Version? MinimumAppVersion { get; init; }      // Minimum WorkTracker version
    public string? IconName { get; init; }                // Icon name for UI (MaterialIcon Kind)
    public IReadOnlyList<string> Tags { get; init; } = [];// Categorization tags
}
```

#### PluginWorklogEntry

```csharp
public class PluginWorklogEntry
{
    public string? TicketId { get; set; }       // Ticket/issue ID (e.g., "PROJ-123")
    public string? Description { get; set; }    // Work description
    public DateTime StartTime { get; set; }     // Start timestamp
    public DateTime EndTime { get; set; }       // End timestamp
    public int DurationMinutes { get; set; }    // Duration in minutes
    public string? Category { get; set; }       // Category/type of work (optional)
    public string? ProjectName { get; set; }    // Project name (optional)
    public Dictionary<string, string>? Metadata { get; set; } // Additional metadata
}
```

#### PluginResult\<T\> and PluginErrorCategory

```csharp
public class PluginResult
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public PluginErrorCategory? ErrorCategory { get; }

    public static PluginResult Success();
    public static PluginResult Failure(string error, PluginErrorCategory category = PluginErrorCategory.Internal);
}

public class PluginResult<T> : PluginResult
{
    public T? Value { get; }

    public static PluginResult<T> Success(T value);
    public static new PluginResult<T> Failure(string error, PluginErrorCategory category = PluginErrorCategory.Internal);
}

public enum PluginErrorCategory
{
    Internal,        // Unexpected internal error
    Validation,      // Invalid input or configuration
    Network,         // Network/connectivity issue
    Authentication,  // Auth failure (expired token, invalid credentials)
    NotFound         // Requested resource not found
}
```

Use `PluginErrorCategory` to provide structured error information. The UI can use the category to show appropriate messages or trigger specific flows (e.g. re-authentication on `Authentication` errors).

#### PluginConfigurationField

```csharp
public class PluginConfigurationField
{
    public required string Key { get; init; }                      // Config key
    public required string Label { get; init; }                    // UI label
    public string? Description { get; init; }                      // Tooltip
    public PluginConfigurationFieldType Type { get; init; }        // Input type
    public bool IsRequired { get; init; }                          // Required?
    public string? DefaultValue { get; init; }                     // Default
    public string? Placeholder { get; init; }                      // Placeholder text
    public string? ValidationPattern { get; init; }                // Validation regex
    public string? ValidationMessage { get; init; }                // Validation error message
}
```

---


## 5. Configuration

`GetConfigurationFields()` returns a list of `PluginConfigurationField` describing what the user fills in the Settings UI. Available field types: `Text`, `Password`, `Url`, `Email`, `Number`, `MultilineText`, `Checkbox`, `Dropdown`.

Password fields are automatically stored in the OS credential manager via `ISecureStorage`.

`PluginBase` always validates `IsRequired` before calling `OnInitializeAsync`. It applies `ValidationPattern` only to required fields; optional fields are not automatically regex-validated. Override `OnValidateConfigurationAsync` to validate optional fields or add any custom validation logic.

See `plugins/WorkTracker.Plugin.Atlassian/TempoWorklogPlugin.cs` for a real example with URL validation, password fields, and optional auto-detected fields.

### 5.1 Reading Configuration

```csharp
// Get optional value (returns null if not found)
var username = GetConfigValue("Username");

// Get required value (throws InvalidOperationException if not found)
var password = GetRequiredConfigValue("Password");

// Parse numeric values manually
var timeout = int.TryParse(GetConfigValue("Timeout"), out var t) ? t : 30;
```

### 5.2 User Configuration (appsettings.json)

Plugin configuration is primarily persisted in the user settings file. The `appsettings.json` `Plugins` section is only used as an initial fallback:

```json
{
  "Plugins": {
    "your-plugin-id": {
      "ApiEndpoint": "https://api.example.com",
      "Username": "john.doe"
    }
  }
}
```

### 5.3 ITokenProviderFactory (Azure AD / MSAL)

Plugins that need Azure AD authentication accept `ITokenProviderFactory` via constructor injection:

```csharp
public class MyCalendarPlugin(
    ILogger<MyCalendarPlugin> logger,
    IHttpClientFactory httpClientFactory,
    ITokenProviderFactory tokenProviderFactory)
    : WorkSuggestionPluginBase(logger)
{
    private ITokenProvider? _tokenProvider;

    protected override async Task<bool> OnInitializeAsync(
        IDictionary<string, string> configuration, CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredConfigValue("TenantId");
        var clientId = GetRequiredConfigValue("ClientId");
        _tokenProvider = await tokenProviderFactory.CreateAsync(tenantId, clientId,
            ["Calendars.Read", "User.Read"]);
        return true;
    }
}
```

`ITokenProvider` provides `AcquireTokenSilentAsync` and `AcquireTokenInteractiveAsync` (device code flow). The factory and MSAL infrastructure are in `Infrastructure/Auth/`.

---

## 6. Testing

For HTTP-based plugins, inject a mock `HttpMessageHandler` via `internal` properties (pattern used by `TempoWorklogPlugin.TempoHttpHandler` / `JiraHttpHandler`). This avoids network calls while testing real plugin logic. To access `internal` members from a test project, add `InternalsVisibleTo` in your plugin `.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="YourPlugin.Tests" />
</ItemGroup>
```

For pure logic (JQL building, data mapping), expose methods as `internal static` and test them directly via `InternalsVisibleTo`.

Reference test suites:
- `tests/WorkTracker.Plugin.Atlassian.Tests/TempoWorklogPluginTests.cs` — HTTP mock handler pattern, retry testing, caching, worklog matching
- `tests/WorkTracker.Plugin.Atlassian.Tests/JiraSuggestionsPluginTests.cs` — pure logic testing (JQL composition, escaping)

---

## 7. Deployment

### 7.1 Build

```bash
dotnet build -c Release
# Output: bin/Release/net10.0/WorkTracker.Plugin.MySystem.dll
```

### 7.2 Installation

Copy plugin DLL to one of the configured plugin directories (default: `<AppContext.BaseDirectory>/plugins/`). Restart WorkTracker.

Additional plugin directories can be configured via `Plugins:Directories` in `appsettings.json`.

---

## 8. Reference Plugins

All plugins in this repository are working examples. Use them as reference instead of synthetic code samples:

| Plugin | Type | Path | Key patterns |
|--------|------|------|-------------|
| **Tempo** | `IWorklogUploadPlugin` | `plugins/WorkTracker.Plugin.Atlassian/TempoWorklogPlugin.cs` | HTTP client lifecycle, retry with exponential backoff, issue ID caching, safe re-initialization, JSON response parsing |
| **Jira Suggestions** | `IWorkSuggestionPlugin` | `plugins/WorkTracker.Plugin.Atlassian/JiraSuggestionsPlugin.cs` | `SupportsSearch`, JQL building, shared `IJiraClient` |
| **Office 365 Calendar** | `IWorkSuggestionPlugin` | `plugins/WorkTracker.Plugin.Office365Calendar/` | MSAL authentication via `ITokenProviderFactory`, device code flow with `IProgress<string>`, Microsoft Graph API |
| **Luxafor** | `IStatusIndicatorPlugin` | `plugins/WorkTracker.Plugin.Luxafor/` | HID device communication, `ILuxaforDeviceFactory`, `SetStateAsync` mapping |
| **GoranG3** | `IWorklogUploadPlugin` | `plugins/WorkTracker.Plugin.GoranG3/` | Simple worklog upload |

Tests: `tests/WorkTracker.Plugin.Atlassian.Tests/`

---

## Resources

- [Plugin Abstractions API](API_DOCUMENTATION.md)
- [Tempo API Documentation](https://tempo-io.github.io/tempo-api-docs/)

---

**Questions?** Open an issue on [GitHub](https://github.com/vesnicancz/work-tracker/issues)

**Last Updated:** April 2026
**Version:** 1.4
