# WorkTracker - Developer Guide

**Comprehensive guide for contributors and developers**

Version: 1.1
Last Updated: March 2026

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Architecture Overview](#2-architecture-overview)
3. [Development Environment](#3-development-environment)
4. [Project Structure](#4-project-structure)
5. [Coding Standards](#5-coding-standards)
6. [Testing](#6-testing)
7. [Database](#7-database)
8. [Adding Features](#8-adding-features)
9. [Pull Request Process](#9-pull-request-process)
10. [Troubleshooting Development Issues](#10-troubleshooting-development-issues)

---

## 1. Getting Started

### 1.1 Prerequisites

**Required:**
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/)
- IDE: [Visual Studio 2022](https://visualstudio.microsoft.com/) or [Rider](https://www.jetbrains.com/rider/)

**Recommended:**
- [SQLite Browser](https://sqlitebrowser.org/) - for database inspection
- [Postman](https://www.postman.com/) - for testing external APIs
- [GitKraken](https://www.gitkraken.com/) or similar Git GUI

### 1.2 Clone and Build

```bash
# Clone repository
git clone https://github.com/yourusername/WorkTracker.git
cd WorkTracker

# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run CLI application
dotnet run --project src/WorkTracker.CLI

# Run WPF application (Windows only)
dotnet run --project src/WorkTracker.WPF

# Run Avalonia application (cross-platform)
dotnet run --project src/WorkTracker.Avalonia
```

### 1.3 Solution Structure

```
WorkTracker.slnx
├── src/
│   ├── WorkTracker.Domain/              # Core business logic
│   ├── WorkTracker.Application/         # Use cases & orchestration
│   ├── WorkTracker.Infrastructure/      # Data access & external services
│   ├── WorkTracker.UI.Shared/           # Shared UI library (models, service interfaces)
│   ├── WorkTracker.CLI/                # Console application
│   ├── WorkTracker.WPF/                # WPF desktop application (Windows)
│   ├── WorkTracker.Avalonia/           # Avalonia desktop application (cross-platform)
│   ├── WorkTracker.Plugin.Abstractions/ # Plugin API
│   └── WorkTracker.Plugin.*/           # Plugin implementations
├── plugins/
│   ├── WorkTracker.Plugin.Luxafor/           # Luxafor LED status indicator
│   ├── WorkTracker.Plugin.Atlassian/         # Tempo/Jira integration
│   └── WorkTracker.Plugin.Office365Calendar/ # O365 Calendar work suggestions
├── tests/
│   ├── WorkTracker.Domain.Tests/
│   ├── WorkTracker.Application.Tests/
│   ├── WorkTracker.Infrastructure.Tests/
│   ├── WorkTracker.UI.Shared.Tests/
│   └── WorkTracker.Plugin.Atlassian.Tests/
└── docs/                                # Documentation
```

---

## 2. Architecture Overview

### 2.1 Clean Architecture

WorkTracker follows **Clean Architecture** (Onion Architecture) principles:

```
┌─────────────────────────────────────────────────┐
│   Presentation Layer (CLI, WPF, Avalonia)        │
│   - User Interface                              │
│   - ViewModels                                  │
│   - Commands                                    │
├─────────────────────────────────────────────────┤
│   UI.Shared Layer                               │
│   - Shared Models & Service Interfaces          │
│   - SettingsService, WorklogStateService        │
│   - WorkSuggestionOrchestrator                  │
│   - SuggestionsViewModel                        │
│   - LocalizationService                         │
├─────────────────────────────────────────────────┤
│   Infrastructure Layer                          │
│   - Data Access (EF Core)                       │
│   - External Services (Tempo, Jira)             │
│   - Dependency Injection Configuration          │
│   - Repository Implementations                  │
├─────────────────────────────────────────────────┤
│   Application Layer                             │
│   - Application Services                        │
│   - Use Cases                                   │
│   - Plugin Manager                              │
│   - DTOs & Result Types                         │
│   - Repository Interfaces                       │
├─────────────────────────────────────────────────┤
│   Domain Layer (Core)                           │
│   - Business Entities (WorkEntry)               │
│   - Domain Logic                                │
│   - Validation Rules                            │
│   - NO EXTERNAL DEPENDENCIES                    │
└─────────────────────────────────────────────────┘
```

**Dependency Rule:**
- **Domain** depends on nothing
- **Application** depends only on Domain
- **Infrastructure** depends on Application & Domain
- **Presentation** depends on all layers

### 2.2 Key Design Patterns

| Pattern | Usage | Location |
|---------|-------|----------|
| **Repository** | Data access abstraction | `IWorkEntryRepository` |
| **Dependency Injection** | Loose coupling | Throughout |
| **Result Pattern** | Functional error handling | `Result<T>` |
| **Strategy** | Plugin system | `IWorklogUploadPlugin`, `IStatusIndicatorPlugin`, `IWorkSuggestionPlugin` |
| **Interface Extraction** | Testable plugin contracts | `ITestablePlugin` for unit-testable plugin logic |
| **MVVM** | WPF & Avalonia presentation | ViewModels |
| **Template Method** | Plugin base classes | `WorklogUploadPluginBase`, `StatusIndicatorPluginBase` |
| **Factory** | DbContext creation | `WorkTrackerDbContextFactory` |

### 2.3 Technology Stack

- **Framework**: .NET 10.0
- **Language**: C# 13 with nullable reference types
- **Database**: SQLite with Entity Framework Core 10.0
- **CLI**: Spectre.Console
- **WPF**: Material Design Themes, CommunityToolkit.Mvvm (Windows only)
- **Avalonia**: Avalonia 11.3, Fluent theme, Material.Icons.Avalonia 3.0, CommunityToolkit.Mvvm (cross-platform)
- **Testing**: xUnit, Moq, FluentAssertions
- **Logging**: Microsoft.Extensions.Logging

---

## 3. Development Environment

### 3.1 Visual Studio Setup

**Extensions:**
1. **ReSharper** or **Roslynator** - Code analysis
2. **CodeMaid** - Code cleanup
3. **SQLite/SQL Server Compact Toolbox** - Database tools
4. **GitFlow** - Branch management

### 3.2 User Secrets for Development

```bash
# Initialize user secrets
cd src/WorkTracker.CLI
dotnet user-secrets init

# Set Tempo/Jira credentials
dotnet user-secrets set "Plugins:tempo:TempoApiToken" "your-dev-token"
dotnet user-secrets set "Plugins:tempo:JiraApiToken" "your-dev-token"
dotnet user-secrets set "Plugins:tempo:JiraEmail" "your-email@example.com"
```

### 3.3 Development Database

**Location:**
```
%LocalAppData%\WorkTracker\worktracker-dev.db
```

**Configuration (appsettings.Development.json):**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "WorkTracker": "Trace"
    }
  },
  "Database": {
    "Path": "%LocalAppData%\\WorkTracker\\worktracker-dev.db"
  }
}
```

**Migrations:**
```bash
# Add migration
dotnet ef migrations add MigrationName --project src/WorkTracker.Infrastructure

# Update database
dotnet ef database update --project src/WorkTracker.Infrastructure

# Generate SQL script
dotnet ef migrations script --project src/WorkTracker.Infrastructure
```

---

## 4. Project Structure

### 4.1 Domain Layer

**Location:** `src/WorkTracker.Domain/`

**Purpose:** Pure business logic with no external dependencies

**Structure:**
```
WorkTracker.Domain/
├── Entities/
│   └── WorkEntry.cs                    # Main business entity
└── WorkTracker.Domain.csproj           # No external dependencies!
```

**Example Entity (controlled mutation with encapsulated setters and factory methods):**

```csharp
namespace WorkTracker.Domain.Entities;

public sealed class WorkEntry
{
    public int Id { get; init; }
    public string? TicketId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; private set; }

    public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

    private WorkEntry() { }

    public bool IsValid() { /* ... */ }

    public void Stop(DateTime endTime, DateTime now) { /* ... */ }
    public void UpdateFields(string? ticketId, DateTime? startTime, DateTime? endTime, string? description, DateTime now) { /* ... */ }

    // Factory method — the only way to create new instances
    public static WorkEntry Create(string? ticketId, DateTime startTime, DateTime? endTime, string? description, DateTime now) { /* ... */ }

    // Reconstitute from persistence (internal)
    internal static WorkEntry Reconstitute(int id, string? ticketId, DateTime startTime, DateTime? endTime, string? description, bool isActive, DateTime createdAt, DateTime? updatedAt = null) { /* ... */ }
}
```

**Rules for Domain Layer:**
- ❌ No dependencies on other layers
- ❌ No infrastructure concerns (DB, HTTP, etc.)
- ✅ Pure C# business logic
- ✅ Validation rules
- ✅ Domain calculations

### 4.2 Application Layer

**Location:** `src/WorkTracker.Application/`

**Purpose:** Use cases, orchestration, application services

**Structure:**
```
WorkTracker.Application/
├── Common/
│   ├── Result.cs                       # Result pattern implementation
│   └── DateTimeHelper.cs               # Utility helpers
├── Interfaces/
│   ├── IWorkEntryService.cs            # Service contracts
│   ├── IWorkEntryRepository.cs         # Repository contracts
│   ├── IWorklogSubmissionService.cs
│   ├── IDateRangeService.cs
│   └── IWorklogValidator.cs
├── Services/
│   ├── WorkEntryService.cs             # Core business service
│   ├── PluginBasedWorklogSubmissionService.cs
│   ├── DateRangeService.cs
│   └── WorklogValidator.cs
├── Plugins/
│   ├── PluginManager.cs                # Plugin lifecycle management
│   └── PluginLoadContext.cs            # Assembly isolation
└── WorkTracker.Application.csproj
```

**Example Service:**

```csharp
namespace WorkTracker.Application.Services;

public class WorkEntryService : IWorkEntryService
{
    private readonly IWorkEntryRepository _repository;
    private readonly ILogger<WorkEntryService> _logger;

    public WorkEntryService(
        IWorkEntryRepository repository,
        ILogger<WorkEntryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result<WorkEntry>> StartWorkAsync(
        string? ticketId,
        DateTime? startTime = null,
        string? description = null,
        DateTime? endTime = null)
    {
        _logger.LogInformation(
            "Starting work on ticket {TicketId} with description {Description}",
            ticketId, description);

        // Auto-stop previous work if creating active entry
        if (!endTime.HasValue)
        {
            var activeEntry = await _repository.GetActiveWorkEntryAsync();
            if (activeEntry != null)
            {
                _logger.LogInformation(
                    "Auto-stopping previous work on ticket {PreviousTicketId}",
                    activeEntry.TicketId);

                var stopTime = DateTimeHelper.RoundToMinute(startTime ?? DateTime.Now);
                activeEntry.EndTime = stopTime;
                activeEntry.IsActive = false;
                activeEntry.UpdatedAt = DateTimeHelper.RoundToMinute(DateTime.Now);

                await _repository.UpdateAsync(activeEntry);
            }
        }

        var workEntry = new WorkEntry
        {
            TicketId = ticketId,
            StartTime = DateTimeHelper.RoundToMinute(startTime ?? DateTime.Now),
            EndTime = DateTimeHelper.RoundToMinute(endTime),
            Description = description,
            IsActive = !endTime.HasValue,
            CreatedAt = DateTimeHelper.RoundToMinute(DateTime.Now)
        };

        if (!workEntry.IsValid())
        {
            _logger.LogWarning("Invalid work entry data");
            return Result.Failure<WorkEntry>(
                "Invalid work entry data. Both ticket ID and description cannot be empty.");
        }

        // Check for overlaps
        if (await _repository.HasOverlappingEntriesAsync(workEntry))
        {
            _logger.LogWarning("Work entry overlaps with existing entry");
            return Result.Failure<WorkEntry>(
                "This work entry overlaps with an existing entry. Please check your times.");
        }

        var result = await _repository.AddAsync(workEntry);
        _logger.LogInformation("Work started successfully with ID {Id}", result.Id);

        return Result.Success(result);
    }

    // ... other methods
}
```

**Rules for Application Layer:**
- ✅ Depends only on Domain
- ✅ Defines repository interfaces
- ✅ Orchestrates business logic
- ✅ Uses Result pattern for error handling
- ❌ No UI concerns
- ❌ No database/HTTP implementation details

### 4.3 Infrastructure Layer

**Location:** `src/WorkTracker.Infrastructure/`

**Purpose:** Data access, external services, DI configuration

**Structure:**
```
WorkTracker.Infrastructure/
├── Data/
│   ├── WorkTrackerDbContext.cs         # EF Core DbContext
│   └── WorkTrackerDbContextFactory.cs  # Design-time factory
├── Repositories/
│   └── WorkEntryRepository.cs          # Repository implementation
├── Migrations/
│   └── *.cs                            # EF Core migrations
├── DependencyInjection.cs              # Service registration
└── WorkTracker.Infrastructure.csproj
```

**Example DbContext:**

```csharp
namespace WorkTracker.Infrastructure.Data;

public class WorkTrackerDbContext : DbContext
{
    public WorkTrackerDbContext(DbContextOptions<WorkTrackerDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkEntry> WorkEntries => Set<WorkEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TicketId)
                .HasMaxLength(100);

            entity.Property(e => e.Description)
                .HasMaxLength(500);

            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.IsActive);

            // Ignore calculated properties
            entity.Ignore(e => e.Duration);
        });
    }
}
```

**Example Repository:**

```csharp
namespace WorkTracker.Infrastructure.Repositories;

public class WorkEntryRepository : IWorkEntryRepository
{
    private readonly WorkTrackerDbContext _context;

    public WorkEntryRepository(WorkTrackerDbContext context)
    {
        _context = context;
    }

    public async Task<WorkEntry?> GetByIdAsync(int id)
    {
        return await _context.WorkEntries.FindAsync(id);
    }

    public async Task<WorkEntry?> GetActiveWorkEntryAsync()
    {
        return await _context.WorkEntries
            .Where(e => e.IsActive)
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        return await _context.WorkEntries
            .Where(e => e.StartTime >= startOfDay && e.StartTime < endOfDay)
            .OrderBy(e => e.StartTime)
            .ToListAsync();
    }

    // ... other methods
}
```

**Dependency Injection:**

```csharp
namespace WorkTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        var dbPath = configuration.GetValue<string>("Database:Path");
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkTracker",
                "worktracker.db");
        }

        var dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        services.AddDbContext<WorkTrackerDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Repositories
        services.AddScoped<IWorkEntryRepository, WorkEntryRepository>();

        // Domain Services
        services.AddScoped<IDateRangeService, DateRangeService>();
        services.AddScoped<IWorklogValidator, WorklogValidator>();

        // Plugin System
        services.AddSingleton<PluginManager>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PluginManager>>();
            var pluginManager = new PluginManager(logger);

            var pluginsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorkTracker",
                "Plugins");

            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
            }

            pluginManager.AddPluginDirectory(pluginsPath);
            return pluginManager;
        });

        // Application Services
        services.AddScoped<IWorkEntryService, WorkEntryService>();
        services.AddScoped<IWorklogSubmissionService, PluginBasedWorklogSubmissionService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WorkTrackerDbContext>();
        await context.Database.MigrateAsync();
    }
}
```

### 4.4 Presentation Layers

#### CLI Application

**Location:** `src/WorkTracker.CLI/`

```
WorkTracker.CLI/
├── CommandHandler.cs                   # Command processing
├── Program.cs                          # Entry point
└── WorkTracker.CLI.csproj
```

#### UI.Shared Library

**Location:** `src/WorkTracker.UI.Shared/`

**Purpose:** Shared UI models, service interfaces, orchestrators, and framework-agnostic service implementations used by both WPF and Avalonia projects.

```
WorkTracker.UI.Shared/
├── Models/                               # Shared UI models
│   ├── ApplicationSettings.cs           # Settings model
│   ├── FavoriteWorkItem.cs              # Favorite templates
│   └── CloseWindowBehavior.cs           # Window close behavior enum
├── Orchestrators/                        # Framework-agnostic business orchestration
│   ├── ISettingsOrchestrator.cs         # Settings operations interface
│   ├── SettingsOrchestrator.cs          # Settings save/load orchestration
│   ├── IWorkEntryEditOrchestrator.cs    # Edit operations interface
│   ├── WorkEntryEditOrchestrator.cs     # Work entry edit orchestration
│   ├── IWorklogSubmissionOrchestrator.cs # Submission operations interface
│   ├── WorklogSubmissionOrchestrator.cs # Worklog submission orchestration
│   ├── IWorkSuggestionOrchestrator.cs   # Work suggestion operations interface
│   ├── WorkSuggestionOrchestrator.cs    # Work suggestion orchestration
│   └── WorkInputParser.cs              # Input parsing logic
├── ViewModels/                           # Shared ViewModels
│   ├── ConfigurationFieldViewModel.cs   # Plugin config field VM
│   ├── PluginViewModel.cs              # Plugin VM
│   ├── SuggestionsViewModel.cs         # Work suggestions VM
│   └── WorklogPreviewItem.cs           # Preview item VM
├── Helpers/
│   └── DurationFormatter.cs            # Duration formatting utility
├── Services/
│   ├── ILocalizationService.cs          # Localization interface
│   ├── LocalizationService.cs           # Localization support
│   ├── ISettingsService.cs              # Settings interface
│   ├── SettingsService.cs               # Application settings management
│   ├── IWorklogStateService.cs          # Worklog state interface
│   ├── WorklogStateService.cs           # Worklog state tracking
│   ├── IDialogService.cs               # Dialog abstraction
│   ├── INotificationService.cs         # Notification abstraction
│   ├── ITrayIconService.cs             # Tray icon abstraction
│   ├── IAutostartManager.cs            # Autostart abstraction
│   ├── IHotkeyService.cs              # Hotkey abstraction
│   └── AppBootstrapper.cs              # App initialization
├── DependencyInjection.cs               # DI registration
└── WorkTracker.UI.Shared.csproj
```

**Dependency graph:**
```
WorkTracker.UI.Shared (net10.0)
  └── WorkTracker.Application → Domain, Plugin.Abstractions

WorkTracker.WPF (net10.0-windows)
  ├── WorkTracker.UI.Shared
  ├── WorkTracker.Infrastructure
  └── WorkTracker.Plugin.Atlassian

WorkTracker.Avalonia (net10.0)
  ├── WorkTracker.UI.Shared
  ├── WorkTracker.Infrastructure
  └── WorkTracker.Plugin.Atlassian
```

#### WPF Application

**Location:** `src/WorkTracker.WPF/`

**Note:** WPF references UI.Shared for models and service interfaces instead of defining its own.

```
WorkTracker.WPF/
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── WorkEntryEditViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── MainWindow.xaml
│   ├── WorkEntryEditDialog.xaml
│   └── SettingsWindow.xaml
├── Services/
│   ├── IDialogService.cs
│   ├── INotificationService.cs
│   └── ITrayIconService.cs
├── Resources/
│   ├── Strings.resx
│   └── Strings.cs.resx
├── App.xaml
└── WorkTracker.WPF.csproj
```

#### Avalonia Application

**Location:** `src/WorkTracker.Avalonia/`

**Purpose:** Cross-platform desktop GUI (Windows, Linux, macOS) with Avalonia 11.3 and Fluent theme.

```
WorkTracker.Avalonia/
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── WorkEntryEditViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── MainWindow.axaml
│   ├── WorkEntryEditDialog.axaml
│   └── SettingsWindow.axaml
├── Services/
│   ├── IDialogService.cs
│   ├── INotificationService.cs
│   └── ITrayIconService.cs
├── Themes/
│   ├── OneDarkPro.axaml                 # Dark theme palette
│   └── OneLight.axaml                   # Light theme palette
├── App.axaml
└── WorkTracker.Avalonia.csproj
```

**Key technologies:**
- Avalonia 11.3 + Fluent theme
- Material.Icons.Avalonia 3.0 for icons
- CommunityToolkit.Mvvm for MVVM
- Switchable Dark/Light themes (One Dark Pro / One Light palettes)
- System tray icon support

---

## 5. Coding Standards

### 5.1 General Principles

**SOLID Principles:**
- ✅ **Single Responsibility** - One class, one responsibility
- ✅ **Open/Closed** - Open for extension, closed for modification
- ✅ **Liskov Substitution** - Substitutable subclasses
- ✅ **Interface Segregation** - Many specific interfaces over one general
- ✅ **Dependency Inversion** - Depend on abstractions

**Clean Code:**
- ✅ Meaningful names (no abbreviations)
- ✅ Small methods (< 50 lines)
- ✅ Single level of abstraction
- ✅ DRY (Don't Repeat Yourself)
- ✅ KISS (Keep It Simple, Stupid)

### 5.2 Naming Conventions

```csharp
// Classes - PascalCase
public class WorkEntryService { }

// Interfaces - I prefix
public interface IWorkEntryRepository { }

// Private fields - _camelCase
private readonly ILogger _logger;

// Public properties - PascalCase
public string TicketId { get; set; }

// Methods - PascalCase
public async Task<Result> StartWorkAsync() { }

// Parameters - camelCase
public void ProcessEntry(WorkEntry workEntry) { }

// Constants - PascalCase or UPPER_CASE
public const int MaxDescriptionLength = 500;

// Local variables - camelCase
var activeEntry = await GetActiveAsync();
```

### 5.3 Code Style

**Braces:**
```csharp
// Always use braces, even for single lines
if (condition)
{
    DoSomething();
}

// Not this:
if (condition) DoSomething();
```

**Async/Await:**
```csharp
// Always use async/await for I/O operations
public async Task<WorkEntry> GetByIdAsync(int id)
{
    return await _context.WorkEntries.FindAsync(id);
}

// Not this:
public WorkEntry GetById(int id)
{
    return _context.WorkEntries.Find(id);
}
```

**Null Handling:**
```csharp
// Use nullable reference types
public async Task<WorkEntry?> GetActiveWorkEntryAsync()
{
    return await _context.WorkEntries
        .Where(e => e.IsActive)
        .FirstOrDefaultAsync();
}

// Check for null
var entry = await GetByIdAsync(id);
if (entry == null)
{
    return Result.Failure<WorkEntry>($"Entry {id} not found");
}
```

**Error Handling:**
```csharp
// Use Result pattern, not exceptions for business logic
public async Task<Result<WorkEntry>> StartWorkAsync(string? ticketId)
{
    if (string.IsNullOrWhiteSpace(ticketId))
    {
        return Result.Failure<WorkEntry>("Ticket ID is required");
    }

    try
    {
        var entry = await CreateEntryAsync(ticketId);
        return Result.Success(entry);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to start work");
        return Result.Failure<WorkEntry>("An error occurred");
    }
}
```

### 5.4 XML Documentation

**Public API must be documented:**

```csharp
/// <summary>
/// Starts tracking work on a specified ticket or task.
/// Automatically stops any currently active work entry.
/// </summary>
/// <param name="ticketId">Jira ticket ID (e.g., PROJ-123). Optional if description is provided.</param>
/// <param name="startTime">Work start time. Defaults to current time if not specified.</param>
/// <param name="description">Work description. Required if ticketId is null.</param>
/// <param name="endTime">Optional end time for creating a completed entry directly.</param>
/// <returns>
/// A <see cref="Result{T}"/> containing the created <see cref="WorkEntry"/> if successful,
/// or an error message if validation fails.
/// </returns>
/// <exception cref="InvalidOperationException">
/// Thrown when a critical database error occurs.
/// </exception>
public async Task<Result<WorkEntry>> StartWorkAsync(
    string? ticketId,
    DateTime? startTime = null,
    string? description = null,
    DateTime? endTime = null)
{
    // Implementation
}
```

### 5.5 Logging

**Log Levels:**

```csharp
// Trace - Very detailed, for debugging only
_logger.LogTrace("Entering method StartWorkAsync with ticketId={TicketId}", ticketId);

// Debug - Detailed information for development
_logger.LogDebug("Active entry found: {EntryId}", activeEntry.Id);

// Information - General flow of application
_logger.LogInformation("Work started successfully with ID {Id}", result.Id);

// Warning - Unexpected but handled situations
_logger.LogWarning("No active work entry found to stop");

// Error - Error that prevented operation
_logger.LogError(ex, "Failed to save work entry");

// Critical - Critical failure requiring immediate attention
_logger.LogCritical(ex, "Database connection lost");
```

**Structured Logging:**

```csharp
// Good - structured logging
_logger.LogInformation(
    "Work entry {EntryId} updated by user {UserId} at {Timestamp}",
    entryId, userId, DateTime.Now);

// Bad - string concatenation
_logger.LogInformation($"Work entry {entryId} updated by user {userId}");
```

---

## 6. Testing

### 6.1 Testing Strategy

**Test Pyramid:**
```
        /\
       /  \  E2E Tests (few)
      /────\
     /      \  Integration Tests (some)
    /────────\
   /          \  Unit Tests (many)
  /────────────\
```

**Coverage Goals:**
- Domain Layer: 90%+
- Application Layer: 80%+
- Infrastructure Layer: 70%+
- Overall: 75%+

### 6.2 Unit Tests

**Location:** `tests/WorkTracker.Application.Tests/`

**Framework:** xUnit + Moq + FluentAssertions

**Example:**

```csharp
using FluentAssertions;
using Moq;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using Xunit;

namespace WorkTracker.Application.Tests.Services;

public class WorkEntryServiceTests
{
    private readonly Mock<IWorkEntryRepository> _repositoryMock;
    private readonly Mock<ILogger<WorkEntryService>> _loggerMock;
    private readonly WorkEntryService _sut; // System Under Test

    public WorkEntryServiceTests()
    {
        _repositoryMock = new Mock<IWorkEntryRepository>();
        _loggerMock = new Mock<ILogger<WorkEntryService>>();
        _sut = new WorkEntryService(_repositoryMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task StartWorkAsync_WithValidTicketId_ReturnsSuccess()
    {
        // Arrange
        var ticketId = "PROJ-123";
        _repositoryMock
            .Setup(r => r.GetActiveWorkEntryAsync())
            .ReturnsAsync((WorkEntry?)null);
        _repositoryMock
            .Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>()))
            .ReturnsAsync(false);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<WorkEntry>()))
            .ReturnsAsync((WorkEntry entry) => entry);

        // Act
        var result = await _sut.StartWorkAsync(ticketId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TicketId.Should().Be(ticketId);
        result.Value.IsActive.Should().BeTrue();
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<WorkEntry>()), Times.Once);
    }

    [Fact]
    public async Task StartWorkAsync_WithoutTicketIdOrDescription_ReturnsFailure()
    {
        // Act
        var result = await _sut.StartWorkAsync(null, description: null);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Both ticket ID and description cannot be empty");
    }

    [Fact]
    public async Task StartWorkAsync_WithActiveEntry_StopsActiveEntry()
    {
        // Arrange
        var activeEntry = new WorkEntry
        {
            Id = 1,
            TicketId = "PROJ-100",
            StartTime = DateTime.Now.AddHours(-1),
            IsActive = true
        };
        _repositoryMock
            .Setup(r => r.GetActiveWorkEntryAsync())
            .ReturnsAsync(activeEntry);
        _repositoryMock
            .Setup(r => r.HasOverlappingEntriesAsync(It.IsAny<WorkEntry>()))
            .ReturnsAsync(false);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<WorkEntry>()))
            .ReturnsAsync((WorkEntry entry) => entry);

        // Act
        var result = await _sut.StartWorkAsync("PROJ-123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<WorkEntry>(
            e => e.Id == activeEntry.Id && !e.IsActive && e.EndTime.HasValue)),
            Times.Once);
    }
}
```

### 6.3 Integration Tests

**Location:** `tests/WorkTracker.Infrastructure.Tests/`

**Example:**

```csharp
public class WorkEntryRepositoryIntegrationTests : IDisposable
{
    private readonly DbContextOptions<WorkTrackerDbContext> _options;
    private readonly WorkTrackerDbContext _context;
    private readonly WorkEntryRepository _repository;

    public WorkEntryRepositoryIntegrationTests()
    {
        _options = new DbContextOptionsBuilder<WorkTrackerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new WorkTrackerDbContext(_options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _repository = new WorkEntryRepository(_context);
    }

    [Fact]
    public async Task GetActiveWorkEntryAsync_WithActiveEntry_ReturnsEntry()
    {
        // Arrange
        var entry = new WorkEntry
        {
            TicketId = "PROJ-123",
            StartTime = DateTime.Now,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _context.WorkEntries.Add(entry);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetActiveWorkEntryAsync();

        // Assert
        result.Should().NotBeNull();
        result!.TicketId.Should().Be("PROJ-123");
        result.IsActive.Should().BeTrue();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
```

### 6.4 Running Tests

```bash
# Run all tests
dotnet test

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

# Run specific test project
dotnet test tests/WorkTracker.Application.Tests

# Run specific test class
dotnet test --filter "FullyQualifiedName~WorkEntryServiceTests"

# Run specific test method
dotnet test --filter "Name=StartWorkAsync_WithValidTicketId_ReturnsSuccess"

# Generate coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:coverage.opencover.xml -targetdir:coverage-report
```

---

## 7. Database

### 7.1 Entity Framework Core Migrations

**Add Migration:**

```bash
# From solution root
dotnet ef migrations add MigrationName --project src/WorkTracker.Infrastructure --startup-project src/WorkTracker.CLI

# Example:
dotnet ef migrations add AddWorkEntryDescription --project src/WorkTracker.Infrastructure --startup-project src/WorkTracker.CLI
```

**Update Database:**

```bash
dotnet ef database update --project src/WorkTracker.Infrastructure --startup-project src/WorkTracker.CLI
```

**Generate SQL Script:**

```bash
dotnet ef migrations script --project src/WorkTracker.Infrastructure --output migration.sql
```

**Remove Last Migration:**

```bash
dotnet ef migrations remove --project src/WorkTracker.Infrastructure
```

### 7.2 Database Schema

**WorkEntry Table:**

```sql
CREATE TABLE WorkEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TicketId TEXT(100),
    StartTime TEXT NOT NULL,
    EndTime TEXT,
    Description TEXT(500),
    IsActive INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT
);

CREATE INDEX IX_WorkEntries_StartTime ON WorkEntries(StartTime);
CREATE INDEX IX_WorkEntries_IsActive ON WorkEntries(IsActive);
```

### 7.3 Seeding Test Data

```csharp
public static class DatabaseSeeder
{
    public static async Task SeedTestDataAsync(WorkTrackerDbContext context)
    {
        if (await context.WorkEntries.AnyAsync())
            return; // Already seeded

        var entries = new[]
        {
            new WorkEntry
            {
                TicketId = "PROJ-123",
                StartTime = DateTime.Today.AddHours(9),
                EndTime = DateTime.Today.AddHours(12),
                Description = "API Implementation",
                IsActive = false,
                CreatedAt = DateTime.Now
            },
            new WorkEntry
            {
                TicketId = "PROJ-124",
                StartTime = DateTime.Today.AddHours(13),
                EndTime = DateTime.Today.AddHours(17),
                Description = "Bug Fixing",
                IsActive = false,
                CreatedAt = DateTime.Now
            }
        };

        context.WorkEntries.AddRange(entries);
        await context.SaveChangesAsync();
    }
}
```

---

## 8. Adding Features

### 8.1 Feature Development Workflow

1. **Create Feature Branch**
   ```bash
   git checkout -b feature/amazing-feature
   ```

2. **Write Failing Test** (TDD approach)
   ```csharp
   [Fact]
   public async Task NewFeature_ShouldWork()
   {
       // Test your feature
   }
   ```

3. **Implement Feature**
   - Start with Domain (if needed)
   - Add Application logic
   - Implement Infrastructure
   - Add Presentation

4. **Make Tests Pass**
   ```bash
   dotnet test
   ```

5. **Refactor**
   - Clean up code
   - Remove duplication
   - Improve naming

6. **Commit**
   ```bash
   git add .
   git commit -m "feat: add amazing feature"
   ```

### 8.2 Example: Adding Tags to WorkEntry

**Step 1: Update Domain**

```csharp
// WorkEntry.cs
public class WorkEntry
{
    // ... existing properties

    public List<string> Tags { get; set; } = new();

    public void AddTag(string tag)
    {
        if (!Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(tag);
        }
    }
}
```

**Step 2: Update Database**

```bash
dotnet ef migrations add AddTagsToWorkEntry --project src/WorkTracker.Infrastructure
dotnet ef database update --project src/WorkTracker.Infrastructure
```

**Step 3: Update Application Service**

```csharp
// IWorkEntryService.cs
Task<Result<WorkEntry>> AddTagAsync(int entryId, string tag);

// WorkEntryService.cs
public async Task<Result<WorkEntry>> AddTagAsync(int entryId, string tag)
{
    var entry = await _repository.GetByIdAsync(entryId);
    if (entry == null)
        return Result.Failure<WorkEntry>("Entry not found");

    entry.AddTag(tag);
    await _repository.UpdateAsync(entry);

    return Result.Success(entry);
}
```

**Step 4: Add CLI Command**

```csharp
// In CommandHandler
case "tag":
    await HandleTagCommandAsync(args);
    break;

private async Task HandleTagCommandAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: worktracker tag <ID> <TAG>");
        return;
    }

    var id = int.Parse(args[1]);
    var tag = args[2];

    var result = await _workEntryService.AddTagAsync(id, tag);
    if (result.IsSuccess)
    {
        Console.WriteLine($"Tag '{tag}' added to entry {id}");
    }
    else
    {
        Console.WriteLine($"Error: {result.Error}");
    }
}
```

**Step 5: Add Tests**

```csharp
[Fact]
public async Task AddTagAsync_WithValidData_AddsTag()
{
    // Arrange
    var entry = new WorkEntry { Id = 1, TicketId = "PROJ-123" };
    _repositoryMock.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(entry);

    // Act
    var result = await _sut.AddTagAsync(1, "bug");

    // Assert
    result.IsSuccess.Should().BeTrue();
    entry.Tags.Should().Contain("bug");
}
```

---

## 9. Pull Request Process

### 9.1 Before Submitting PR

**Checklist:**
- [ ] Code builds successfully
- [ ] All tests pass
- [ ] Code coverage meets standards (75%+)
- [ ] No compiler warnings
- [ ] Code formatted consistently
- [ ] XML documentation added for public APIs
- [ ] Commit messages follow convention
- [ ] Branch is up-to-date with master

### 9.2 Commit Message Convention

**Format:**
```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting)
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Build/tooling changes

**Examples:**

```
feat(cli): add tag command for work entries

Implement new 'tag' command that allows users to add tags
to existing work entries for better categorization.

Closes #123
```

```
fix(service): prevent duplicate worklogs in Tempo

Check for existing worklogs before submitting to avoid
duplicates in Tempo API.

Fixes #456
```

### 9.3 PR Description Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
Describe tests performed

## Checklist
- [ ] Code builds successfully
- [ ] All tests pass
- [ ] Documentation updated
- [ ] Follows coding standards

## Screenshots (if applicable)
Add screenshots for UI changes
```

### 9.4 Code Review Process

**As Author:**
1. Self-review your code before requesting review
2. Ensure CI passes
3. Respond to review comments promptly
4. Make requested changes in new commits

**As Reviewer:**
1. Check for:
   - Correctness
   - Performance
   - Security
   - Maintainability
   - Test coverage
2. Ask questions if unclear
3. Suggest improvements
4. Approve when satisfied

---

## 10. Troubleshooting Development Issues

### 10.1 Build Errors

**"The type or namespace name 'X' could not be found"**

```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

**"Project file is incomplete"**

```bash
# Repair SDK
dotnet --list-sdks
dotnet --info
# Reinstall .NET 10.0 SDK if needed
```

### 10.2 Database Issues

**"Database is locked"**

- Close all instances of WorkTracker
- Delete `.db-wal` and `.db-shm` files
- Restart application

**"No such table: WorkEntries"**

```bash
# Run migrations
dotnet ef database update --project src/WorkTracker.Infrastructure
```

### 10.3 Test Failures

**"Cannot create DbContext"**

- Ensure you're using in-memory SQLite for tests
- Check that connection is opened before EnsureCreated

**"Mock setup not matched"**

- Verify mock setup matches actual call
- Use `It.IsAny<T>()` for flexible matching
- Check Verify statements

---

## Resources

- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [Entity Framework Core](https://docs.microsoft.com/ef/core/)
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [xUnit Documentation](https://xunit.net/)
- [Moq Documentation](https://github.com/moq/moq4)

---

**Last Updated:** March 2026
**Version:** 1.1
