# WorkTracker - API Documentation

**Complete API reference for WorkTracker**

Version: 1.0
Last Updated: November 2025

---

## Table of Contents

1. [Domain Layer API](#1-domain-layer-api)
2. [Application Layer API](#2-application-layer-api)
3. [Plugin Abstractions API](#3-plugin-abstractions-api)
4. [Result Pattern](#4-result-pattern)
5. [Exceptions](#5-exceptions)

---

## 1. Domain Layer API

### 1.1 WorkEntry

**Namespace:** `WorkTracker.Domain.Entities`

Main business entity representing a work tracking entry.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `int` | Unique identifier (auto-generated) |
| `TicketId` | `string?` | Jira ticket ID (e.g., "PROJ-123") |
| `StartTime` | `DateTime` | When work started |
| `EndTime` | `DateTime?` | When work ended (null if active) |
| `Description` | `string?` | Work description |
| `IsActive` | `bool` | Whether entry is currently active |
| `CreatedAt` | `DateTime` | Creation timestamp |
| `UpdatedAt` | `DateTime?` | Last update timestamp |
| `Duration` | `TimeSpan?` | Calculated duration (EndTime - StartTime) |

#### Methods

##### IsValid()

Validates the work entry according to business rules.

```csharp
public bool IsValid()
```

**Returns:** `bool` - True if valid, false otherwise

**Validation Rules:**
- At least one of `TicketId` or `Description` must be provided
- If `EndTime` is set, it must be after `StartTime`

**Example:**

```csharp
var entry = new WorkEntry
{
    TicketId = "PROJ-123",
    StartTime = DateTime.Now,
    IsActive = true,
    CreatedAt = DateTime.Now
};

if (entry.IsValid())
{
    // Entry is valid
}
```

---

## 2. Application Layer API

### 2.1 IWorkEntryService

**Namespace:** `WorkTracker.Application.Interfaces`

Main service interface for managing work entries.

#### Methods

##### StartWorkAsync

Starts tracking work on a ticket or task.

```csharp
Task<Result<WorkEntry>> StartWorkAsync(
    string? ticketId,
    DateTime? startTime = null,
    string? description = null,
    DateTime? endTime = null)
```

**Parameters:**
- `ticketId` (`string?`) - Jira ticket ID (e.g., "PROJ-123"). Optional if description is provided.
- `startTime` (`DateTime?`) - Work start time. Defaults to current time.
- `description` (`string?`) - Work description. Required if ticketId is null.
- `endTime` (`DateTime?`) - Optional end time for creating completed entry.

**Returns:** `Task<Result<WorkEntry>>` - Result containing created entry or error

**Behavior:**
- Automatically stops any active work entry
- Validates that at least one of ticketId or description is provided
- Checks for overlapping time entries
- Rounds times to nearest minute

**Example:**

```csharp
// Start work on ticket
var result = await _service.StartWorkAsync("PROJ-123");
if (result.IsSuccess)
{
    Console.WriteLine($"Started work with ID: {result.Value.Id}");
}

// Start work with description only
var result2 = await _service.StartWorkAsync(
    ticketId: null,
    description: "Team meeting");

// Create completed entry
var result3 = await _service.StartWorkAsync(
    "PROJ-456",
    startTime: DateTime.Today.AddHours(9),
    endTime: DateTime.Today.AddHours(12));
```

##### StopWorkAsync

Stops tracking the currently active work entry.

```csharp
Task<Result<WorkEntry>> StopWorkAsync(DateTime? endTime = null)
```

**Parameters:**
- `endTime` (`DateTime?`) - End time. Defaults to current time.

**Returns:** `Task<Result<WorkEntry>>` - Result containing stopped entry or error

**Example:**

```csharp
// Stop current work
var result = await _service.StopWorkAsync();

// Stop with specific time
var result2 = await _service.StopWorkAsync(DateTime.Now.AddHours(-1));
```

##### GetActiveWorkAsync

Gets the currently active work entry.

```csharp
Task<WorkEntry?> GetActiveWorkAsync()
```

**Returns:** `Task<WorkEntry?>` - Active entry or null if none

**Example:**

```csharp
var activeEntry = await _service.GetActiveWorkAsync();
if (activeEntry != null)
{
    Console.WriteLine($"Working on: {activeEntry.TicketId}");
}
```

##### GetWorkEntriesByDateAsync

Gets all work entries for a specific date.

```csharp
Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateAsync(DateTime date)
```

**Parameters:**
- `date` (`DateTime`) - Target date

**Returns:** `Task<IEnumerable<WorkEntry>>` - List of entries for the date

**Example:**

```csharp
// Get today's entries
var entries = await _service.GetWorkEntriesByDateAsync(DateTime.Today);

// Get yesterday's entries
var yesterday = await _service.GetWorkEntriesByDateAsync(
    DateTime.Today.AddDays(-1));
```

##### GetWorkEntriesByDateRangeAsync

Gets all work entries within a date range.

```csharp
Task<IEnumerable<WorkEntry>> GetWorkEntriesByDateRangeAsync(
    DateTime startDate,
    DateTime endDate)
```

**Parameters:**
- `startDate` (`DateTime`) - Range start (inclusive)
- `endDate` (`DateTime`) - Range end (inclusive)

**Returns:** `Task<IEnumerable<WorkEntry>>` - List of entries in range

**Example:**

```csharp
// Get this week's entries
var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
var weekEnd = weekStart.AddDays(6);
var weekEntries = await _service.GetWorkEntriesByDateRangeAsync(
    weekStart, weekEnd);
```

##### UpdateWorkEntryAsync

Updates an existing work entry.

```csharp
Task<Result<WorkEntry>> UpdateWorkEntryAsync(
    int id,
    string? ticketId = null,
    DateTime? startTime = null,
    DateTime? endTime = null,
    string? description = null)
```

**Parameters:**
- `id` (`int`) - Entry ID to update
- `ticketId` (`string?`) - New ticket ID (or null to clear)
- `startTime` (`DateTime?`) - New start time
- `endTime` (`DateTime?`) - New end time (or null to make active)
- `description` (`string?`) - New description (or null to clear)

**Returns:** `Task<Result<WorkEntry>>` - Result with updated entry or error

**Example:**

```csharp
// Update ticket ID
var result = await _service.UpdateWorkEntryAsync(
    id: 5,
    ticketId: "PROJ-999");

// Update times
var result2 = await _service.UpdateWorkEntryAsync(
    id: 5,
    startTime: DateTime.Today.AddHours(9),
    endTime: DateTime.Today.AddHours(17));
```

##### DeleteWorkEntryAsync

Deletes a work entry.

```csharp
Task<Result> DeleteWorkEntryAsync(int id)
```

**Parameters:**
- `id` (`int`) - Entry ID to delete

**Returns:** `Task<Result>` - Success or error result

**Example:**

```csharp
var result = await _service.DeleteWorkEntryAsync(5);
if (result.IsSuccess)
{
    Console.WriteLine("Entry deleted successfully");
}
```

### 2.2 IWorklogSubmissionService

**Namespace:** `WorkTracker.Application.Interfaces`

Service for submitting worklogs to external systems via plugins.

#### Methods

##### SubmitDailyWorklogsAsync

Submits worklogs for a specific date.

```csharp
Task<Result<WorklogSubmissionSummary>> SubmitDailyWorklogsAsync(
    DateTime date,
    string? providerId = null)
```

**Parameters:**
- `date` (`DateTime`) - Target date
- `providerId` (`string?`) - Plugin ID (null for default)

**Returns:** `Task<Result<WorklogSubmissionSummary>>` - Submission results

**Example:**

```csharp
// Submit today to default provider
var result = await _service.SubmitDailyWorklogsAsync(DateTime.Today);

// Submit to specific provider (e.g., Tempo)
var result2 = await _service.SubmitDailyWorklogsAsync(
    DateTime.Today,
    "tempo");

if (result.IsSuccess)
{
    var summary = result.Value;
    Console.WriteLine($"Submitted: {summary.SuccessCount}/{summary.TotalCount}");
}
```

##### SubmitWeeklyWorklogsAsync

Submits worklogs for a week.

```csharp
Task<Result<WorklogSubmissionSummary>> SubmitWeeklyWorklogsAsync(
    DateTime weekStart,
    string? providerId = null)
```

**Parameters:**
- `weekStart` (`DateTime`) - Week start date (Monday)
- `providerId` (`string?`) - Plugin ID

**Returns:** `Task<Result<WorklogSubmissionSummary>>` - Submission results

##### PreviewDailyWorklogsAsync

Previews worklogs before submission (dry-run).

```csharp
Task<Result<List<PluginWorklogEntry>>> PreviewDailyWorklogsAsync(
    DateTime date,
    string? providerId = null)
```

**Parameters:**
- `date` (`DateTime`) - Target date
- `providerId` (`string?`) - Plugin ID

**Returns:** `Task<Result<List<PluginWorklogEntry>>>` - Worklogs to be submitted

**Example:**

```csharp
// Preview before submitting
var preview = await _service.PreviewDailyWorklogsAsync(DateTime.Today);
if (preview.IsSuccess)
{
    foreach (var worklog in preview.Value)
    {
        Console.WriteLine($"{worklog.IssueKey}: {worklog.TimeSpent}");
    }

    // If looks good, submit
    var result = await _service.SubmitDailyWorklogsAsync(DateTime.Today);
}
```

### 2.3 IWorkEntryRepository

**Namespace:** `WorkTracker.Application.Interfaces`

Repository interface for data access abstraction.

#### Methods

##### GetByIdAsync

```csharp
Task<WorkEntry?> GetByIdAsync(int id)
```

##### GetActiveWorkEntryAsync

```csharp
Task<WorkEntry?> GetActiveWorkEntryAsync()
```

##### GetByDateAsync

```csharp
Task<IEnumerable<WorkEntry>> GetByDateAsync(DateTime date)
```

##### GetByDateRangeAsync

```csharp
Task<IEnumerable<WorkEntry>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
```

##### AddAsync

```csharp
Task<WorkEntry> AddAsync(WorkEntry workEntry)
```

##### UpdateAsync

```csharp
Task UpdateAsync(WorkEntry workEntry)
```

##### DeleteAsync

```csharp
Task DeleteAsync(int id)
```

##### HasOverlappingEntriesAsync

```csharp
Task<bool> HasOverlappingEntriesAsync(WorkEntry workEntry)
```

Checks if the given entry overlaps with existing entries.

---

## 3. Plugin Abstractions API

### 3.1 IPlugin

**Namespace:** `WorkTracker.Plugin.Abstractions`

Base interface for all plugins.

#### Properties

##### Metadata

```csharp
PluginMetadata Metadata { get; }
```

Plugin identification and information.

#### Methods

##### InitializeAsync

```csharp
Task<bool> InitializeAsync(Dictionary<string, string>? configuration = null)
```

Initialize plugin with configuration.

**Parameters:**
- `configuration` - Configuration key-value pairs

**Returns:** `Task<bool>` - True if successful

##### ValidateConfigurationAsync

```csharp
Task<PluginResult<bool>> ValidateConfigurationAsync(
    Dictionary<string, string> configuration)
```

Validate configuration without initializing.

##### ShutdownAsync

```csharp
Task ShutdownAsync()
```

Cleanup and shutdown plugin.

### 3.2 IWorklogUploadPlugin

**Namespace:** `WorkTracker.Plugin.Abstractions`

Interface for worklog upload plugins.

#### Methods

##### GetConfigurationFields

```csharp
List<PluginConfigurationField> GetConfigurationFields()
```

Get required configuration fields for UI.

**Returns:** List of configuration field definitions

##### TestConnectionAsync

```csharp
Task<PluginResult<bool>> TestConnectionAsync()
```

Test connection to external system.

**Returns:** Success/failure result

##### UploadWorklogAsync

```csharp
Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog)
```

Upload single worklog.

**Parameters:**
- `worklog` - Worklog entry to upload

**Returns:** Success/failure result

##### UploadWorklogsAsync

```csharp
Task<PluginResult<List<WorklogSubmissionResult>>> UploadWorklogsAsync(
    List<PluginWorklogEntry> worklogs)
```

Upload multiple worklogs (batch).

**Parameters:**
- `worklogs` - List of worklog entries

**Returns:** Results for each worklog

##### GetWorklogsAsync

```csharp
Task<PluginResult<List<PluginWorklogEntry>>> GetWorklogsAsync(
    DateTime startDate,
    DateTime endDate)
```

Retrieve existing worklogs from external system.

**Parameters:**
- `startDate` - Range start
- `endDate` - Range end

**Returns:** List of existing worklogs

##### WorklogExistsAsync

```csharp
Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog)
```

Check if worklog already exists in external system.

**Parameters:**
- `worklog` - Worklog to check

**Returns:** True if exists

### 3.3 WorklogUploadPluginBase

**Namespace:** `WorkTracker.Plugin.Abstractions`

Abstract base class providing helper methods.

#### Protected Methods

##### GetConfigValue

```csharp
protected string? GetConfigValue(string key)
```

Get configuration value (returns null if not found).

##### GetRequiredConfigValue

```csharp
protected string GetRequiredConfigValue(string key)
```

Get required configuration value (throws if not found).

##### GetConfigValue<T>

```csharp
protected T GetConfigValue<T>(string key, T defaultValue)
```

Get typed configuration value with default.

##### EnsureInitialized

```csharp
protected void EnsureInitialized()
```

Throws if plugin not initialized.

#### Protected Properties

##### Logger

```csharp
protected ILogger? Logger { get; }
```

Logger instance (if available).

##### Configuration

```csharp
protected Dictionary<string, string> Configuration { get; }
```

Current configuration.

### 3.4 Data Types

#### PluginMetadata

```csharp
public class PluginMetadata
{
    public string Id { get; set; }              // Unique identifier
    public string Name { get; set; }            // Display name
    public string Version { get; set; }         // Semantic version (1.0.0)
    public string Author { get; set; }          // Author name
    public string Description { get; set; }     // Short description
    public string[] Tags { get; set; }          // Tags for categorization
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

#### PluginConfigurationField

```csharp
public class PluginConfigurationField
{
    public string Key { get; set; }                      // Config key
    public string DisplayName { get; set; }              // UI label
    public string? Description { get; set; }             // Tooltip/help text
    public PluginConfigurationFieldType Type { get; set; } // Input type
    public bool IsRequired { get; set; }                 // Required field?
    public string? ValidationRegex { get; set; }         // Validation pattern
    public string? DefaultValue { get; set; }            // Default value
}
```

#### PluginConfigurationFieldType

```csharp
public enum PluginConfigurationFieldType
{
    Text,       // Single-line text input
    Password,   // Password input (masked)
    Url,        // URL input with validation
    Number,     // Numeric input
    Boolean     // Checkbox/toggle
}
```

#### WorklogSubmissionResult

```csharp
public class WorklogSubmissionResult
{
    public string IssueKey { get; set; }        // Ticket ID
    public DateTime Date { get; set; }          // Work date
    public TimeSpan TimeSpent { get; set; }     // Duration
    public bool Success { get; set; }           // Upload succeeded?
    public string? Error { get; set; }          // Error message if failed
}
```

---

## 4. Result Pattern

### 4.1 Result<T>

**Namespace:** `WorkTracker.Application.Common`

Functional result type for error handling without exceptions.

#### Properties

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }              // True if successful
    public T? Value { get; }                    // Value (if success)
    public string? Error { get; }               // Error message (if failure)
}
```

#### Static Methods

##### Success

```csharp
public static Result<T> Success(T value)
```

Create successful result with value.

##### Failure

```csharp
public static Result<T> Failure(string error)
```

Create failed result with error message.

#### Example Usage

```csharp
// Service method returning Result
public async Task<Result<WorkEntry>> StartWorkAsync(string? ticketId)
{
    if (string.IsNullOrWhiteSpace(ticketId))
    {
        return Result<WorkEntry>.Failure("Ticket ID is required");
    }

    var entry = await CreateEntryAsync(ticketId);
    return Result<WorkEntry>.Success(entry);
}

// Consuming code
var result = await _service.StartWorkAsync("PROJ-123");

if (result.IsSuccess)
{
    var entry = result.Value;
    Console.WriteLine($"Started: {entry.TicketId}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

### 4.2 Result (non-generic)

For operations that don't return a value:

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    public static Result Success()
    public static Result Failure(string error)
}
```

**Example:**

```csharp
public async Task<Result> DeleteWorkEntryAsync(int id)
{
    var entry = await _repository.GetByIdAsync(id);
    if (entry == null)
        return Result.Failure("Entry not found");

    await _repository.DeleteAsync(id);
    return Result.Success();
}
```

### 4.3 PluginResult<T>

**Namespace:** `WorkTracker.Plugin.Abstractions`

Plugin-specific result type (similar to Result<T>).

```csharp
public class PluginResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    public static PluginResult<T> Success(T value)
    public static PluginResult<T> Failure(string error)
}
```

---

## 5. Exceptions

### 5.1 Exception Handling Strategy

WorkTracker uses the **Result pattern** for expected errors and exceptions for unexpected errors.

**Use Result for:**
- Validation failures
- Business rule violations
- Expected error conditions

**Use Exceptions for:**
- Infrastructure failures (database, network)
- Programming errors (null reference, etc.)
- Unexpected conditions

### 5.2 Common Exceptions

#### InvalidOperationException

Thrown when operation is invalid in current state.

```csharp
public void EnsureInitialized()
{
    if (!_isInitialized)
        throw new InvalidOperationException("Plugin not initialized");
}
```

#### ArgumentException

Thrown for invalid arguments.

```csharp
public WorkEntry GetById(int id)
{
    if (id <= 0)
        throw new ArgumentException("ID must be positive", nameof(id));

    // ...
}
```

#### NotSupportedException

Thrown for unsupported operations.

```csharp
public override Task<PluginResult<List<PluginWorklogEntry>>> GetWorklogsAsync(...)
{
    throw new NotSupportedException("This plugin does not support worklog retrieval");
}
```

---

## API Examples

### Complete Workflow Example

```csharp
using WorkTracker.Application.Interfaces;
using WorkTracker.Domain.Entities;

public class WorkflowExample
{
    private readonly IWorkEntryService _workEntryService;
    private readonly IWorklogSubmissionService _submissionService;

    public async Task DailyWorkflowAsync()
    {
        // 1. Start work in morning
        var startResult = await _workEntryService.StartWorkAsync(
            "PROJ-123",
            startTime: DateTime.Today.AddHours(9),
            description: "Implementing new feature");

        if (!startResult.IsSuccess)
        {
            Console.WriteLine($"Failed to start: {startResult.Error}");
            return;
        }

        Console.WriteLine($"Started work: {startResult.Value.Id}");

        // 2. Take a lunch break (stop and start new)
        var lunchResult = await _workEntryService.StartWorkAsync(
            description: "Lunch break");

        // 3. Resume work after lunch
        var resumeResult = await _workEntryService.StartWorkAsync("PROJ-123");

        // 4. At end of day, stop work
        var stopResult = await _workEntryService.StopWorkAsync(
            DateTime.Today.AddHours(17));

        // 5. Review day's work
        var entries = await _workEntryService.GetWorkEntriesByDateAsync(DateTime.Today);
        var totalTime = entries
            .Where(e => e.Duration.HasValue)
            .Sum(e => e.Duration!.Value.TotalHours);

        Console.WriteLine($"Total work today: {totalTime:F2} hours");

        // 6. Preview submission
        var preview = await _submissionService.PreviewDailyWorklogsAsync(DateTime.Today);
        if (preview.IsSuccess)
        {
            Console.WriteLine($"Will submit {preview.Value.Count} worklogs");
        }

        // 7. Submit to Tempo
        var submitResult = await _submissionService.SubmitDailyWorklogsAsync(
            DateTime.Today,
            "tempo");

        if (submitResult.IsSuccess)
        {
            var summary = submitResult.Value;
            Console.WriteLine(
                $"Submitted: {summary.SuccessCount}/{summary.TotalCount} worklogs");
        }
    }
}
```

---

## Resources

- [GitHub Repository](https://github.com/yourusername/WorkTracker)
- [User Guide](USER_GUIDE.md)
- [Developer Guide](DEVELOPER_GUIDE.md)
- [Plugin Development Guide](PLUGIN_DEVELOPMENT.md)

---

**Last Updated:** November 2025
**Version:** 1.0
