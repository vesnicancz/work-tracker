# WorkTracker - Plugin Development Guide

**Complete guide to creating WorkTracker plugins**

Version: 1.1
Last Updated: March 2026

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Plugin Architecture](#2-plugin-architecture)
3. [Quick Start](#3-quick-start)
4. [Plugin Abstractions](#4-plugin-abstractions)
5. [Creating a Worklog Upload Plugin](#5-creating-a-worklog-upload-plugin)
6. [Plugin Configuration](#6-plugin-configuration)
7. [Testing Plugins](#7-testing-plugins)
8. [Deployment](#8-deployment)
9. [Best Practices](#9-best-practices)
10. [Example Plugins](#10-example-plugins)

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

Plugins cannot:
- ❌ Modify WorkTracker's core behavior
- ❌ Access other plugins directly
- ❌ Directly manipulate the database
- ❌ Override UI components

### 1.3 Plugin Types

Currently, WorkTracker supports:

1. **IWorklogUploadPlugin** - Upload work logs to external systems
2. **Custom Plugin Types** - Planned for future releases

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

Plugins are discovered from:

1. **Embedded Plugins** - Compiled into main application
   ```
   src/WorkTracker.Infrastructure/DependencyInjection.cs
   → LoadEmbeddedPlugin<TempoWorklogPlugin>()
   ```

2. **External Plugins** - Loaded from directory
   ```
   %AppData%\WorkTracker\Plugins\
   └── MyPlugin.dll
   ```

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
    /// Initialize plugin with configuration
    /// </summary>
    Task<bool> InitializeAsync(Dictionary<string, string>? configuration = null);

    /// <summary>
    /// Validate configuration without initializing
    /// </summary>
    Task<PluginResult<bool>> ValidateConfigurationAsync(
        Dictionary<string, string> configuration);

    /// <summary>
    /// Shutdown and cleanup resources
    /// </summary>
    Task ShutdownAsync();
}
```

### 4.2 IWorklogUploadPlugin Interface

**Specialized interface for worklog upload:**

```csharp
public interface IWorklogUploadPlugin : IPlugin
{
    /// <summary>
    /// Get required configuration fields
    /// </summary>
    List<PluginConfigurationField> GetConfigurationFields();

    /// <summary>
    /// Test connection to external system
    /// </summary>
    Task<PluginResult<bool>> TestConnectionAsync();

    /// <summary>
    /// Upload single worklog
    /// </summary>
    Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog);

    /// <summary>
    /// Upload multiple worklogs
    /// </summary>
    Task<PluginResult<List<WorklogSubmissionResult>>> UploadWorklogsAsync(
        List<PluginWorklogEntry> worklogs);

    /// <summary>
    /// Get worklogs for date range
    /// </summary>
    Task<PluginResult<List<PluginWorklogEntry>>> GetWorklogsAsync(
        DateTime startDate,
        DateTime endDate);

    /// <summary>
    /// Check if worklog already exists
    /// </summary>
    Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog);
}
```

### 4.3 WorklogUploadPluginBase

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

### 4.4 Core Types

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

## 6. Plugin Configuration

### 6.1 Configuration Fields

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

### 6.2 Reading Configuration

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

### 6.3 User Configuration (appsettings.json)

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

### 6.4 Configuration Validation

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

## 7. Testing Plugins

### 7.1 Unit Tests

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

### 7.2 Integration Tests

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

## 8. Deployment

### 8.1 Build Release

```bash
# Build release version
dotnet build -c Release

# Output will be in:
bin/Release/net9.0/WorkTracker.Plugin.MySystem.dll
```

### 8.2 Installation

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
    <file src="bin/Release/net9.0/*.dll" target="lib/net9.0" />
  </files>
</package>
```

```bash
# Create package
nuget pack MyPlugin.nuspec

# Install
nuget install WorkTracker.Plugin.MySystem -OutputDirectory %AppData%\WorkTracker\Plugins
```

### 8.3 Distribution

**GitHub Releases:**
1. Create release on GitHub
2. Attach plugin DLL as asset
3. Users download and copy to plugins folder

**NuGet Gallery:**
1. Publish to NuGet.org
2. Users install via package manager

---

## 9. Best Practices

### 9.1 Error Handling

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

### 9.2 Logging

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

### 9.3 Resource Management

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

### 9.4 API Rate Limiting

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

### 9.5 Security

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

## 10. Example Plugins

### 10.1 Tempo Plugin (Built-in)

See `plugins/WorkTracker.Plugin.Tempo/` for complete example.

### 10.2 Minimal Mock Plugin

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

**Questions?** Open an issue on [GitHub](https://github.com/yourusername/WorkTracker/issues)

**Last Updated:** November 2025
**Version:** 1.0
