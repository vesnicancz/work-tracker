# WorkTracker - Plugin Development Guide

**Complete guide to creating WorkTracker plugins**

Version: 1.2
Last Updated: March 2026

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Plugin Architecture](#2-plugin-architecture)
3. [Quick Start](#3-quick-start)
4. [Plugin Abstractions](#4-plugin-abstractions)
5. [Creating a Worklog Upload Plugin](#5-creating-a-worklog-upload-plugin)
6. [Creating a Work Suggestion Plugin](#6-creating-a-work-suggestion-plugin)
7. [Plugin Configuration](#7-plugin-configuration)
8. [Testing Plugins](#8-testing-plugins)
9. [Deployment](#9-deployment)
10. [Best Practices](#10-best-practices)
11. [Example Plugins](#11-example-plugins)

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
│  1. Discovery                                    │
│     - PluginManager scans plugin directories    │
│     - Finds DLLs implementing IPlugin          │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  2. Loading                                      │
│     - Creates isolated AssemblyLoadContext      │
│     - Loads plugin assembly                     │
│     - Instantiates plugin class                 │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  3. Initialization                               │
│     - Calls InitializeAsync()                   │
│     - Passes configuration from appsettings     │
│     - Plugin validates config                   │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  4. Usage                                        │
│     - Application calls plugin methods          │
│     - Plugin performs work (upload, etc.)       │
│     - Returns results                           │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│  5. Shutdown                                     │
│     - Calls ShutdownAsync()                     │
│     - Plugin cleans up resources                │
│     - AssemblyLoadContext unloaded              │
└─────────────────────────────────────────────────┘
```

### 2.2 Assembly Isolation

Plugins are loaded in **isolated AssemblyLoadContexts**, which means:

✅ **Benefits:**
- Plugin dependencies don't conflict with host app
- Plugins can use different versions of same library
- Plugins can be unloaded at runtime

⚠️ **Limitations:**
- Shared types must be in abstractions assembly
- Can't directly pass plugin-specific types to host

### 2.3 Plugin Discovery

All plugins are standalone projects discovered via **directory scanning** using `DiscoverAndLoadPlugins()`.

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

```csharp
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.MySystem;

public class MySystemPlugin : WorklogUploadPluginBase
{
    public override PluginMetadata Metadata => new()
    {
        Id = "mysystem",
        Name = "My System Integration",
        Version = "1.0.0",
        Author = "Your Name",
        Description = "Uploads worklogs to My System",
        Tags = new[] { "time-tracking", "integration" }
    };

    protected override List<PluginConfigurationField> GetConfigurationFieldsInternal()
    {
        return new List<PluginConfigurationField>
        {
            new()
            {
                Key = "ApiUrl",
                DisplayName = "API URL",
                Description = "Base URL for My System API",
                Type = PluginConfigurationFieldType.Url,
                IsRequired = true,
                ValidationRegex = @"^https?://.*"
            },
            new()
            {
                Key = "ApiKey",
                DisplayName = "API Key",
                Description = "Your My System API key",
                Type = PluginConfigurationFieldType.Password,
                IsRequired = true
            }
        };
    }

    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog)
    {
        try
        {
            var apiUrl = GetRequiredConfigValue("ApiUrl");
            var apiKey = GetRequiredConfigValue("ApiKey");

            // Your upload logic here
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var response = await client.PostAsJsonAsync(
                $"{apiUrl}/worklogs",
                new
                {
                    issueKey = worklog.IssueKey,
                    date = worklog.Date,
                    timeSpentSeconds = (int)worklog.TimeSpent.TotalSeconds,
                    description = worklog.Description
                });

            if (response.IsSuccessStatusCode)
            {
                return PluginResult<bool>.Success(true);
            }

            return PluginResult<bool>.Failure(
                $"API returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to upload worklog");
            return PluginResult<bool>.Failure($"Upload failed: {ex.Message}");
        }
    }
}
```

### 3.3 Build and Deploy

```bash
# Build plugin
dotnet build -c Release

# Copy to plugin directory
copy bin/Release/net10.0/WorkTracker.Plugin.MySystem.dll %AppData%\WorkTracker\Plugins\
```

### 3.4 Configure Plugin

Edit `appsettings.json`:

```json
{
  "Plugins": {
    "mysystem": {
      "ApiUrl": "https://api.mysystem.com",
      "ApiKey": "your-api-key-here"
    }
  }
}
```

---

## 4. Plugin Abstractions

### 4.1 IPlugin Interface

**Base interface for all plugins:**

```csharp
public interface IPlugin
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
    /// Shutdown and cleanup resources
    /// </summary>
    Task ShutdownAsync();
}
```

### 4.2 ITestablePlugin Interface

**Spolecny interface pro pluginy s testovanim pripojeni. Dedi z nej IWorklogUploadPlugin i IWorkSuggestionPlugin:**

```csharp
public interface ITestablePlugin : IPlugin
{
    Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken);

    // Overload s progress reportingem pro OAuth flows (device code apod.)
    Task<PluginResult<bool>> TestConnectionAsync(
        IProgress<string>? progress, CancellationToken cancellationToken);
}
```

Base tridy (`WorklogUploadPluginBase`, `WorkSuggestionPluginBase`) poskytují výchozí implementaci progress overloadu, která deleguje na verzi bez progressu. Pluginy vyžadující interakci (např. Office 365 Calendar s device code flow) přepíší progress overload a reportují stav přihlášení do Settings UI.

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

**Abstraktni base class pro suggestion pluginy. Dedi z `PluginBase` (sdilena konfigurace, validace, lifecycle):**

```csharp
public abstract class WorkSuggestionPluginBase : PluginBase, IWorkSuggestionPlugin
{
    // Povinne k implementaci
    public abstract Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken);
    public abstract Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(
        DateTime date, CancellationToken cancellationToken);

    // Volitelne - vychozi je false / not supported
    public virtual bool SupportsSearch => false;
    public virtual Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(
        string query, CancellationToken cancellationToken);
}
```

Minimalni implementace vyzaduje pouze `Metadata`, `GetConfigurationFieldsInternal()`, `TestConnectionAsync()` a `GetSuggestionsAsync()`. Search je opt-in.

### 4.8 WorklogUploadPluginBase

**Abstract base class with helper methods:**

```csharp
public abstract class WorklogUploadPluginBase : IWorklogUploadPlugin
{
    protected ILogger? Logger { get; private set; }
    protected Dictionary<string, string> Configuration { get; private set; }

    // Abstract members to implement
    public abstract PluginMetadata Metadata { get; }
    protected abstract List<PluginConfigurationField> GetConfigurationFieldsInternal();
    protected abstract Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog);

    // Helper methods provided
    protected string? GetConfigValue(string key);
    protected string GetRequiredConfigValue(string key);
    protected T GetConfigValue<T>(string key, T defaultValue);

    // Default implementations (can override)
    public virtual Task<PluginResult<bool>> TestConnectionAsync();
    public virtual Task<PluginResult<List<WorklogSubmissionResult>>> UploadWorklogsAsync(
        List<PluginWorklogEntry> worklogs);
}
```

### 4.9 Core Types

#### PluginMetadata

```csharp
public class PluginMetadata
{
    public string Id { get; set; }              // Unique identifier
    public string Name { get; set; }            // Display name
    public string Version { get; set; }         // Semantic version
    public string Author { get; set; }          // Author name
    public string Description { get; set; }     // Short description
    public string[] Tags { get; set; }          // Categorization tags
}
```

#### PluginWorklogEntry

```csharp
public class PluginWorklogEntry
{
    public string IssueKey { get; set; }        // Ticket/issue ID
    public DateTime Date { get; set; }          // Work date
    public TimeSpan TimeSpent { get; set; }     // Duration
    public DateTime StartTime { get; set; }     // Start timestamp
    public string? Description { get; set; }    // Optional description
}
```

#### PluginResult<T>

```csharp
public class PluginResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    public static PluginResult<T> Success(T value);
    public static PluginResult<T> Failure(string error);
}
```

#### PluginConfigurationField

```csharp
public class PluginConfigurationField
{
    public string Key { get; set; }                      // Config key
    public string DisplayName { get; set; }              // UI label
    public string? Description { get; set; }             // Tooltip
    public PluginConfigurationFieldType Type { get; set; } // Input type
    public bool IsRequired { get; set; }                 // Required?
    public string? ValidationRegex { get; set; }         // Validation
    public string? DefaultValue { get; set; }            // Default
}
```

---

## 5. Creating a Worklog Upload Plugin

### 5.1 Minimal Implementation

```csharp
using WorkTracker.Plugin.Abstractions;

public class MinimalPlugin : WorklogUploadPluginBase
{
    // 1. Define metadata
    public override PluginMetadata Metadata => new()
    {
        Id = "minimal",
        Name = "Minimal Plugin",
        Version = "1.0.0",
        Author = "Me"
    };

    // 2. Define config fields
    protected override List<PluginConfigurationField> GetConfigurationFieldsInternal()
    {
        return new List<PluginConfigurationField>
        {
            new()
            {
                Key = "ApiUrl",
                DisplayName = "API URL",
                Type = PluginConfigurationFieldType.Url,
                IsRequired = true
            }
        };
    }

    // 3. Implement upload
    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog)
    {
        var apiUrl = GetRequiredConfigValue("ApiUrl");

        // Your logic here
        await Task.Delay(100); // Simulate API call

        return PluginResult<bool>.Success(true);
    }
}
```

### 5.2 Advanced Implementation

**Full-featured plugin with all bells and whistles:**

```csharp
using System.Net.Http.Json;
using WorkTracker.Plugin.Abstractions;
using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Advanced;

public class AdvancedPlugin : WorklogUploadPluginBase
{
    private HttpClient? _httpClient;
    private string? _accountId;

    public override PluginMetadata Metadata => new()
    {
        Id = "advanced",
        Name = "Advanced Integration",
        Version = "1.0.0",
        Author = "Your Company",
        Description = "Full-featured integration with external system",
        Tags = new[] { "time-tracking", "advanced" }
    };

    #region Configuration

    protected override List<PluginConfigurationField> GetConfigurationFieldsInternal()
    {
        return new List<PluginConfigurationField>
        {
            new()
            {
                Key = "BaseUrl",
                DisplayName = "Base URL",
                Description = "API base URL (e.g., https://api.example.com)",
                Type = PluginConfigurationFieldType.Url,
                IsRequired = true,
                ValidationRegex = @"^https://.*",
                DefaultValue = "https://api.example.com"
            },
            new()
            {
                Key = "ApiToken",
                DisplayName = "API Token",
                Description = "Your API authentication token",
                Type = PluginConfigurationFieldType.Password,
                IsRequired = true
            },
            new()
            {
                Key = "AccountId",
                DisplayName = "Account ID",
                Description = "Your account ID (leave empty for auto-detection)",
                Type = PluginConfigurationFieldType.Text,
                IsRequired = false
            },
            new()
            {
                Key = "Timeout",
                DisplayName = "Request Timeout (seconds)",
                Type = PluginConfigurationFieldType.Number,
                IsRequired = false,
                DefaultValue = "30"
            }
        };
    }

    #endregion

    #region Initialization

    public override async Task<bool> InitializeAsync(
        Dictionary<string, string>? configuration = null)
    {
        var result = await base.InitializeAsync(configuration);
        if (!result)
            return false;

        try
        {
            // Initialize HTTP client
            var baseUrl = GetRequiredConfigValue("BaseUrl");
            var apiToken = GetRequiredConfigValue("ApiToken");
            var timeout = GetConfigValue("Timeout", 30);

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(timeout)
            };

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorkTracker/1.0");

            // Auto-detect account ID if not provided
            _accountId = GetConfigValue("AccountId");
            if (string.IsNullOrEmpty(_accountId))
            {
                _accountId = await DetectAccountIdAsync();
                Logger?.LogInformation("Auto-detected account ID: {AccountId}", _accountId);
            }

            Logger?.LogInformation("Plugin initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to initialize plugin");
            return false;
        }
    }

    private async Task<string> DetectAccountIdAsync()
    {
        EnsureInitialized();

        var response = await _httpClient!.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserInfo>();
        return user?.AccountId ?? throw new InvalidOperationException("Could not detect account ID");
    }

    #endregion

    #region Connection Testing

    public override async Task<PluginResult<bool>> TestConnectionAsync()
    {
        try
        {
            EnsureInitialized();

            var response = await _httpClient!.GetAsync("/api/health");

            if (response.IsSuccessStatusCode)
            {
                Logger?.LogInformation("Connection test successful");
                return PluginResult<bool>.Success(true);
            }

            return PluginResult<bool>.Failure(
                $"Connection test failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Connection test failed");
            return PluginResult<bool>.Failure($"Connection error: {ex.Message}");
        }
    }

    #endregion

    #region Worklog Upload

    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
        PluginWorklogEntry worklog)
    {
        try
        {
            EnsureInitialized();

            // Check if worklog already exists
            var existsResult = await WorklogExistsAsync(worklog);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                Logger?.LogWarning("Worklog already exists for {IssueKey} on {Date}",
                    worklog.IssueKey, worklog.Date);
                return PluginResult<bool>.Failure("Worklog already exists");
            }

            // Build request
            var request = new
            {
                issueKey = worklog.IssueKey,
                accountId = _accountId,
                date = worklog.Date.ToString("yyyy-MM-dd"),
                timeSpentSeconds = (int)worklog.TimeSpent.TotalSeconds,
                startTime = worklog.StartTime.ToString("HH:mm"),
                description = worklog.Description
            };

            // Send request
            var response = await _httpClient!.PostAsJsonAsync("/api/worklogs", request);

            if (response.IsSuccessStatusCode)
            {
                Logger?.LogInformation("Worklog uploaded: {IssueKey} - {Duration}",
                    worklog.IssueKey, worklog.TimeSpent);
                return PluginResult<bool>.Success(true);
            }

            var error = await response.Content.ReadAsStringAsync();
            Logger?.LogError("Upload failed: {StatusCode} - {Error}",
                response.StatusCode, error);
            return PluginResult<bool>.Failure($"Upload failed: {error}");
        }
        catch (HttpRequestException ex)
        {
            Logger?.LogError(ex, "HTTP request failed");
            return PluginResult<bool>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Upload failed");
            return PluginResult<bool>.Failure($"Upload failed: {ex.Message}");
        }
    }

    #endregion

    #region Worklog Retrieval

    public override async Task<PluginResult<List<PluginWorklogEntry>>> GetWorklogsAsync(
        DateTime startDate,
        DateTime endDate)
    {
        try
        {
            EnsureInitialized();

            var url = $"/api/worklogs?accountId={_accountId}" +
                      $"&from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}";

            var response = await _httpClient!.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var worklogs = await response.Content.ReadFromJsonAsync<List<ExternalWorklog>>();
            if (worklogs == null)
                return PluginResult<List<PluginWorklogEntry>>.Success(new List<PluginWorklogEntry>());

            var result = worklogs.Select(w => new PluginWorklogEntry
            {
                IssueKey = w.IssueKey,
                Date = DateTime.Parse(w.Date),
                TimeSpent = TimeSpan.FromSeconds(w.TimeSpentSeconds),
                StartTime = DateTime.Parse($"{w.Date}T{w.StartTime}"),
                Description = w.Description
            }).ToList();

            return PluginResult<List<PluginWorklogEntry>>.Success(result);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to retrieve worklogs");
            return PluginResult<List<PluginWorklogEntry>>.Failure(
                $"Retrieval failed: {ex.Message}");
        }
    }

    public override async Task<PluginResult<bool>> WorklogExistsAsync(
        PluginWorklogEntry worklog)
    {
        try
        {
            var result = await GetWorklogsAsync(worklog.Date, worklog.Date);
            if (!result.IsSuccess)
                return PluginResult<bool>.Failure(result.Error!);

            var exists = result.Value!.Any(w =>
                w.IssueKey == worklog.IssueKey &&
                w.Date.Date == worklog.Date.Date &&
                Math.Abs((w.TimeSpent - worklog.TimeSpent).TotalMinutes) < 1);

            return PluginResult<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to check worklog existence");
            return PluginResult<bool>.Failure($"Check failed: {ex.Message}");
        }
    }

    #endregion

    #region Batch Upload with Retry

    public override async Task<PluginResult<List<WorklogSubmissionResult>>> UploadWorklogsAsync(
        List<PluginWorklogEntry> worklogs)
    {
        var results = new List<WorklogSubmissionResult>();

        foreach (var worklog in worklogs)
        {
            var result = await UploadWorklogWithRetryAsync(worklog);

            results.Add(new WorklogSubmissionResult
            {
                IssueKey = worklog.IssueKey,
                Date = worklog.Date,
                TimeSpent = worklog.TimeSpent,
                Success = result.IsSuccess,
                Error = result.Error
            });

            // Rate limiting - be nice to the API
            if (result.IsSuccess)
            {
                await Task.Delay(200);
            }
        }

        return PluginResult<List<WorklogSubmissionResult>>.Success(results);
    }

    private async Task<PluginResult<bool>> UploadWorklogWithRetryAsync(
        PluginWorklogEntry worklog,
        int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var result = await UploadWorklogAsync(worklog);

            if (result.IsSuccess)
                return result;

            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                Logger?.LogWarning(
                    "Upload failed, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})",
                    delay.TotalSeconds, attempt, maxRetries);
                await Task.Delay(delay);
            }
        }

        return PluginResult<bool>.Failure("Upload failed after retries");
    }

    #endregion

    #region Cleanup

    public override async Task ShutdownAsync()
    {
        await base.ShutdownAsync();

        _httpClient?.Dispose();
        _httpClient = null;

        Logger?.LogInformation("Plugin shut down successfully");
    }

    #endregion

    #region DTOs

    private class UserInfo
    {
        public string? AccountId { get; set; }
    }

    private class ExternalWorklog
    {
        public string IssueKey { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public int TimeSpentSeconds { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    #endregion
}
```

---

## 6. Creating a Work Suggestion Plugin

### 6.1 Minimalni implementace

```csharp
using WorkTracker.Plugin.Abstractions;

public class MyCalendarPlugin : WorkSuggestionPluginBase
{
    public override PluginMetadata Metadata => new()
    {
        Id = "my-calendar",
        Name = "My Calendar Suggestions",
        Version = new Version(1, 0, 0),
        Author = "Your Name",
        Description = "Navrhy pracovnich zaznamu z kalendare",
        IconName = "CalendarMonth",
        Tags = ["calendar", "suggestions"]
    };

    public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields()
    {
        return
        [
            new()
            {
                Key = "ApiUrl",
                Label = "API URL",
                Type = PluginConfigurationFieldType.Url,
                IsRequired = true
            }
        ];
    }

    public override async Task<PluginResult<bool>> TestConnectionAsync(
        CancellationToken cancellationToken)
    {
        // Overeni pripojeni k API
        return PluginResult<bool>.Success(true);
    }

    public override async Task<PluginResult<IReadOnlyList<WorkSuggestion>>> GetSuggestionsAsync(
        DateTime date, CancellationToken cancellationToken)
    {
        var suggestions = new List<WorkSuggestion>
        {
            new()
            {
                Title = "Daily standup",
                StartTime = date.Date.AddHours(9),
                EndTime = date.Date.AddHours(9).AddMinutes(15),
                Source = Metadata.Name,
                SourceId = "event-123"
            }
        };

        return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(suggestions);
    }
}
```

### 6.2 Plugin s podporou vyhledavani (Search)

Pokud plugin podporuje textove vyhledavani (napr. Jira issues), staci overridnout `SupportsSearch` a `SearchAsync`:

```csharp
public class MyIssueTrackerPlugin : WorkSuggestionPluginBase
{
    // ... Metadata, config, TestConnectionAsync, GetSuggestionsAsync ...

    public override bool SupportsSearch => true;

    public override async Task<PluginResult<IReadOnlyList<WorkSuggestion>>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        // Vyhledavani issues podle textu
        var results = await SearchIssuesAsync(query, cancellationToken);

        var suggestions = results.Select(issue => new WorkSuggestion
        {
            Title = issue.Summary,
            TicketId = issue.Key,
            Source = Metadata.Name,
            SourceId = issue.Key,
            SourceUrl = $"https://jira.example.com/browse/{issue.Key}"
        }).ToList();

        return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(suggestions);
    }
}
```

---

## 7. Plugin Configuration

### 7.1 Configuration Fields

Define what configuration your plugin needs:

```csharp
protected override List<PluginConfigurationField> GetConfigurationFieldsInternal()
{
    return new List<PluginConfigurationField>
    {
        // Text field
        new()
        {
            Key = "Username",
            DisplayName = "Username",
            Type = PluginConfigurationFieldType.Text,
            IsRequired = true
        },

        // Password field (masked input)
        new()
        {
            Key = "Password",
            DisplayName = "Password",
            Type = PluginConfigurationFieldType.Password,
            IsRequired = true
        },

        // URL field (with validation)
        new()
        {
            Key = "ApiEndpoint",
            DisplayName = "API Endpoint",
            Type = PluginConfigurationFieldType.Url,
            ValidationRegex = @"^https?://.*",
            IsRequired = true
        },

        // Number field
        new()
        {
            Key = "Timeout",
            DisplayName = "Timeout (seconds)",
            Type = PluginConfigurationFieldType.Number,
            DefaultValue = "30"
        },

        // Boolean field
        new()
        {
            Key = "EnableRetry",
            DisplayName = "Enable automatic retry",
            Type = PluginConfigurationFieldType.Boolean,
            DefaultValue = "true"
        }
    };
}
```

### 7.2 Reading Configuration

```csharp
// In your plugin methods:

// Get optional value (returns null if not found)
var username = GetConfigValue("Username");

// Get required value (throws if not found)
var password = GetRequiredConfigValue("Password");

// Get with default value
var timeout = GetConfigValue("Timeout", 30);

// Get typed value
var enableRetry = GetConfigValue<bool>("EnableRetry", true);
```

### 7.3 User Configuration (appsettings.json)

```json
{
  "Plugins": {
    "your-plugin-id": {
      "ApiEndpoint": "https://api.example.com",
      "Username": "john.doe",
      "Password": "secret",
      "Timeout": "60",
      "EnableRetry": "true"
    }
  }
}
```

### 7.4 Configuration Validation

```csharp
public override async Task<PluginResult<bool>> ValidateConfigurationAsync(
    Dictionary<string, string> configuration)
{
    // Check required fields
    if (!configuration.ContainsKey("ApiToken"))
        return PluginResult<bool>.Failure("ApiToken is required");

    // Validate format
    if (configuration.TryGetValue("ApiEndpoint", out var endpoint))
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            return PluginResult<bool>.Failure("ApiEndpoint must be a valid URL");
    }

    // Test connection
    try
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization",
            $"Bearer {configuration["ApiToken"]}");

        var response = await client.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
            return PluginResult<bool>.Failure("Connection test failed");
    }
    catch (Exception ex)
    {
        return PluginResult<bool>.Failure($"Connection error: {ex.Message}");
    }

    return PluginResult<bool>.Success(true);
}
```

---

## 8. Testing Plugins

### 8.1 Unit Tests

```csharp
using Xunit;
using FluentAssertions;

namespace WorkTracker.Plugin.MySystem.Tests;

public class MySystemPluginTests
{
    [Fact]
    public void Metadata_ShouldHaveCorrectValues()
    {
        // Arrange & Act
        var plugin = new MySystemPlugin();

        // Assert
        plugin.Metadata.Id.Should().Be("mysystem");
        plugin.Metadata.Name.Should().NotBeNullOrEmpty();
        plugin.Metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void GetConfigurationFields_ShouldReturnRequiredFields()
    {
        // Arrange
        var plugin = new MySystemPlugin();

        // Act
        var fields = plugin.GetConfigurationFields();

        // Assert
        fields.Should().Contain(f => f.Key == "ApiUrl");
        fields.Should().Contain(f => f.Key == "ApiKey");
        fields.Should().OnlyContain(f => f.IsRequired);
    }

    [Fact]
    public async Task InitializeAsync_WithValidConfig_ReturnsTrue()
    {
        // Arrange
        var plugin = new MySystemPlugin();
        var config = new Dictionary<string, string>
        {
            ["ApiUrl"] = "https://api.test.com",
            ["ApiKey"] = "test-key"
        };

        // Act
        var result = await plugin.InitializeAsync(config);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UploadWorklogAsync_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var plugin = new MySystemPlugin();
        await plugin.InitializeAsync(new Dictionary<string, string>
        {
            ["ApiUrl"] = "https://api.test.com",
            ["ApiKey"] = "test-key"
        });

        var worklog = new PluginWorklogEntry
        {
            IssueKey = "PROJ-123",
            Date = DateTime.Today,
            TimeSpent = TimeSpan.FromHours(2),
            StartTime = DateTime.Today.AddHours(9)
        };

        // Act
        var result = await plugin.UploadWorklogAsync(worklog);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
```

### 8.2 Testovani Work Suggestion pluginu

Dobrym vzorem je testovani internich pure metod (napr. JQL builder) bez nutnosti mockovat HTTP volani. Viz `WorkTracker.Plugin.Atlassian.Tests`:

```csharp
using Xunit;
using FluentAssertions;
using WorkTracker.Plugin.Atlassian;

public class JiraSuggestionsPluginTests
{
    private const string DefaultFilter = "assignee = currentUser() AND status != Done ORDER BY updated DESC";

    [Fact]
    public void BuildSearchJql_CombinesBaseFilterWithTextSearch()
    {
        var jql = JiraSuggestionsPlugin.BuildSearchJql("fix", DefaultFilter);

        jql.Should().Contain("(assignee = currentUser() AND status != Done)");
        jql.Should().Contain("key ~ \"fix*\"");
    }

    [Fact]
    public void BuildSearchJql_EscapesQuotesInQuery()
    {
        var jql = JiraSuggestionsPlugin.BuildSearchJql("test\"value", DefaultFilter);

        jql.Should().Contain("test\\\"value*\"");
    }

    [Fact]
    public void BuildSearchJql_StripsAsterisksFromQuery()
    {
        var jql = JiraSuggestionsPlugin.BuildSearchJql("PROJ*-123", DefaultFilter);

        jql.Should().Contain("PROJ-123*\"");
    }
}
```

Metoda `BuildSearchJql` je `internal static`, testovatelna pres `InternalsVisibleTo`. Tento pattern umoznuje pokryt logiku kompozice JQL dotazu bez zavislosti na siti.

### 8.3 Integration Tests

```csharp
public class MySystemPluginIntegrationTests : IAsyncLifetime
{
    private MySystemPlugin? _plugin;
    private TestServer? _testServer;

    public async Task InitializeAsync()
    {
        // Setup test HTTP server
        _testServer = new TestServer();
        await _testServer.StartAsync();

        // Initialize plugin with test server
        _plugin = new MySystemPlugin();
        await _plugin.InitializeAsync(new Dictionary<string, string>
        {
            ["ApiUrl"] = _testServer.Url,
            ["ApiKey"] = "test-key"
        });
    }

    [Fact]
    public async Task UploadWorklog_RealAPI_Success()
    {
        // Arrange
        var worklog = new PluginWorklogEntry
        {
            IssueKey = "TEST-123",
            Date = DateTime.Today,
            TimeSpent = TimeSpan.FromHours(1),
            StartTime = DateTime.Today.AddHours(9)
        };

        // Act
        var result = await _plugin!.UploadWorklogAsync(worklog);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify API was called
        _testServer!.Requests.Should().ContainSingle(r =>
            r.Path == "/api/worklogs" &&
            r.Method == "POST");
    }

    public async Task DisposeAsync()
    {
        if (_plugin != null)
            await _plugin.ShutdownAsync();

        await _testServer!.DisposeAsync();
    }
}
```

---

## 9. Deployment

### 9.1 Build Release

```bash
# Build release version
dotnet build -c Release

# Output will be in:
bin/Release/net10.0/WorkTracker.Plugin.MySystem.dll
```

### 9.2 Installation

**Option 1: Manual Installation**

1. Copy plugin DLL to:
   ```
   %AppData%\WorkTracker\Plugins\WorkTracker.Plugin.MySystem.dll
   ```

2. Restart WorkTracker

**Option 2: Installer Package**

Create NuGet package:

```xml
<!-- MyPlugin.nuspec -->
<package>
  <metadata>
    <id>WorkTracker.Plugin.MySystem</id>
    <version>1.0.0</version>
    <authors>Your Name</authors>
    <description>My System integration for WorkTracker</description>
    <dependencies>
      <dependency id="WorkTracker.Plugin.Abstractions" version="1.0.0" />
    </dependencies>
  </metadata>
  <files>
    <file src="bin/Release/net10.0/*.dll" target="lib/net10.0" />
  </files>
</package>
```

```bash
# Create package
nuget pack MyPlugin.nuspec

# Install
nuget install WorkTracker.Plugin.MySystem -OutputDirectory %AppData%\WorkTracker\Plugins
```

### 9.3 Distribution

**GitHub Releases:**
1. Create release on GitHub
2. Attach plugin DLL as asset
3. Users download and copy to plugins folder

**NuGet Gallery:**
1. Publish to NuGet.org
2. Users install via package manager

---

## 10. Best Practices

### 10.1 Error Handling

```csharp
// ✅ Good - Return PluginResult
protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
    PluginWorklogEntry worklog)
{
    try
    {
        // Upload logic
        return PluginResult<bool>.Success(true);
    }
    catch (HttpRequestException ex)
    {
        Logger?.LogError(ex, "Network error");
        return PluginResult<bool>.Failure($"Network error: {ex.Message}");
    }
    catch (Exception ex)
    {
        Logger?.LogError(ex, "Unexpected error");
        return PluginResult<bool>.Failure($"Error: {ex.Message}");
    }
}

// ❌ Bad - Don't throw exceptions
protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
    PluginWorklogEntry worklog)
{
    throw new Exception("This will crash the host app!");
}
```

### 10.2 Logging

```csharp
// ✅ Good - Use structured logging
Logger?.LogInformation(
    "Uploading worklog for {IssueKey} on {Date} ({Duration})",
    worklog.IssueKey,
    worklog.Date,
    worklog.TimeSpent);

// ❌ Bad - String concatenation
Logger?.LogInformation($"Uploading {worklog.IssueKey}");
```

### 10.3 Resource Management

```csharp
public class MyPlugin : WorklogUploadPluginBase
{
    private HttpClient? _httpClient;

    public override async Task<bool> InitializeAsync(...)
    {
        _httpClient = new HttpClient();
        return true;
    }

    public override async Task ShutdownAsync()
    {
        _httpClient?.Dispose();
        _httpClient = null;

        await base.ShutdownAsync();
    }
}
```

### 10.4 API Rate Limiting

```csharp
private readonly SemaphoreSlim _rateLimiter = new(5, 5); // 5 concurrent requests

private async Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> action)
{
    await _rateLimiter.WaitAsync();
    try
    {
        return await action();
    }
    finally
    {
        _rateLimiter.Release();
        await Task.Delay(200); // 200ms between requests
    }
}
```

### 10.5 Security

```csharp
// ✅ Never log sensitive data
Logger?.LogInformation("Connecting to {Url}", baseUrl);
// NOT: Logger?.LogInformation("Using token {Token}", apiToken);

// ✅ Validate input
protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(
    PluginWorklogEntry worklog)
{
    if (string.IsNullOrWhiteSpace(worklog.IssueKey))
        return PluginResult<bool>.Failure("IssueKey is required");

    if (worklog.TimeSpent <= TimeSpan.Zero)
        return PluginResult<bool>.Failure("TimeSpent must be positive");

    // ...
}
```

---

## 11. Example Plugins

### 11.1 Atlassian Plugin (Worklog Upload + Work Suggestions)

Adresar `plugins/WorkTracker.Plugin.Atlassian/` obsahuje dva pluginy v jednom baliku:

- **TempoWorklogPlugin** - Upload worklogs pres Tempo API (`IWorklogUploadPlugin`)
- **JiraSuggestionsPlugin** - Navrhy pracovnich zaznamu z Jira issues (`IWorkSuggestionPlugin`, `SupportsSearch = true`)

Oba sdileji `JiraClient` pro komunikaci s Jira REST API. Testy v `tests/WorkTracker.Plugin.Atlassian.Tests/`.

### 11.2 Office 365 Calendar Plugin (Work Suggestions)

See `plugins/WorkTracker.Plugin.Office365Calendar/` for a calendar-based suggestion plugin. Pouziva MSAL pro autentizaci vuci Microsoft Graph API a vraci udalosti z kalendare jako `WorkSuggestion` s `StartTime`/`EndTime`.

### 11.3 Luxafor Plugin (Status Indicator)

See `plugins/WorkTracker.Plugin.Luxafor/` for a complete status indicator plugin example. Uses the `Luxafor.HidSharp` library (`src/Luxafor.HidSharp/`) for HID device communication.

### 11.4 GoranG3 Plugin (Worklog Upload)

See `plugins/WorkTracker.Plugin.GoranG3/` for another worklog upload plugin example.

### 11.5 Minimal Mock Plugin

```csharp
public class MockPlugin : WorklogUploadPluginBase
{
    public override PluginMetadata Metadata => new()
    {
        Id = "mock",
        Name = "Mock Plugin",
        Version = "1.0.0",
        Author = "WorkTracker"
    };

    protected override List<PluginConfigurationField> GetConfigurationFieldsInternal()
    {
        return new List<PluginConfigurationField>
        {
            new() { Key = "Delay", DisplayName = "Simulated Delay (ms)", Type = PluginConfigurationFieldType.Number, DefaultValue = "100" }
        };
    }

    protected override async Task<PluginResult<bool>> UploadWorklogInternalAsync(PluginWorklogEntry worklog)
    {
        var delay = GetConfigValue("Delay", 100);
        await Task.Delay(delay);

        Logger?.LogInformation("Mock upload: {IssueKey}", worklog.IssueKey);
        return PluginResult<bool>.Success(true);
    }
}
```

---

## Resources

- [WorkTracker Plugin Abstractions API](API_DOCUMENTATION.md)
- [Tempo API Documentation](https://tempo-io.github.io/tempo-api-docs/)
- [HttpClient Best Practices](https://docs.microsoft.com/aspnet/core/fundamentals/http-requests)

---

**Questions?** Open an issue on [GitHub](https://github.com/vesnicancz/work-tracker/issues)

**Last Updated:** March 2026
**Version:** 1.2
