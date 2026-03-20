using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service that manages the current worklog tracking state of the application.
/// Provides a single source of truth for active work tracking.
/// </summary>
public interface IWorklogStateService
{
	/// <summary>
	/// Gets whether the service has been initialized
	/// </summary>
	bool IsInitialized { get; }

	/// <summary>
	/// Gets the currently active work entry, or null if no work is being tracked
	/// </summary>
	WorkEntry? ActiveWork { get; }

	/// <summary>
	/// Gets whether work is currently being tracked
	/// </summary>
	bool IsTracking { get; }

	/// <summary>
	/// Initializes the service by loading the current state from the database.
	/// Must be called before using any other methods or properties.
	/// </summary>
	Task InitializeAsync();

	/// <summary>
	/// Starts tracking a new work entry with the current time.
	/// Automatically notifies about state changes.
	/// </summary>
	Task<Result<WorkEntry>> StartTrackingAsync(string? ticketId, string? description);

	/// <summary>
	/// Stops tracking the currently active work entry.
	/// Automatically notifies about state changes.
	/// </summary>
	Task<Result> StopTrackingAsync();

	/// <summary>
	/// Creates a new work entry with custom start/end times.
	/// Automatically notifies about state changes.
	/// </summary>
	Task<Result<WorkEntry>> CreateWorkEntryAsync(
		string? ticketId,
		DateTime startTime,
		string? description,
		DateTime? endTime);

	/// <summary>
	/// Updates an existing work entry.
	/// Automatically notifies about state changes.
	/// </summary>
	Task<Result> UpdateWorkEntryAsync(
		int id,
		string? ticketId,
		DateTime startTime,
		DateTime? endTime,
		string? description);

	/// <summary>
	/// Deletes a work entry.
	/// Automatically notifies about state changes.
	/// </summary>
	Task<Result> DeleteWorkEntryAsync(int id);

	/// <summary>
	/// Refreshes the state from the database.
	/// </summary>
	Task RefreshFromDatabaseAsync();

	/// <summary>
	/// Event raised when the active work entry changes
	/// </summary>
	event EventHandler<WorkEntry?>? ActiveWorkChanged;

	/// <summary>
	/// Event raised when the tracking state changes
	/// </summary>
	event EventHandler<bool>? IsTrackingChanged;

	/// <summary>
	/// Event raised when work entries have been modified (created, updated, deleted).
	/// </summary>
	event EventHandler? WorkEntriesModified;

	/// <summary>
	/// Notifies that work entries have been modified by external operations.
	/// </summary>
	void NotifyWorkEntriesModified();
}
