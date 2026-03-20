using WorkTracker.Application.Common;
using WorkTracker.Domain.Entities;

namespace WorkTracker.WPF.Services;

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
	/// <returns>A task representing the asynchronous operation</returns>
	Task InitializeAsync();

	/// <summary>
	/// Starts tracking a new work entry with the current time.
	/// This is for "start tracking NOW" scenarios.
	/// Automatically notifies about state changes.
	/// </summary>
	/// <param name="ticketId">Optional ticket/issue ID</param>
	/// <param name="description">Optional description of the work</param>
	/// <returns>Result containing the created WorkEntry or an error</returns>
	Task<Result<WorkEntry>> StartTrackingAsync(string? ticketId, string? description);

	/// <summary>
	/// Stops tracking the currently active work entry.
	/// Automatically notifies about state changes.
	/// </summary>
	/// <returns>Result indicating success or failure</returns>
	Task<Result> StopTrackingAsync();

	/// <summary>
	/// Creates a new work entry with custom start/end times.
	/// This is for creating historical or scheduled entries.
	/// Automatically notifies about state changes.
	/// </summary>
	/// <param name="ticketId">Optional ticket/issue ID</param>
	/// <param name="startTime">Start time for the entry</param>
	/// <param name="description">Optional description of the work</param>
	/// <param name="endTime">Optional end time (null = active entry)</param>
	/// <returns>Result containing the created WorkEntry or an error</returns>
	Task<Result<WorkEntry>> CreateWorkEntryAsync(
		string? ticketId,
		DateTime startTime,
		string? description,
		DateTime? endTime);

	/// <summary>
	/// Updates an existing work entry.
	/// Automatically notifies about state changes.
	/// </summary>
	/// <param name="id">ID of the work entry to update</param>
	/// <param name="ticketId">Optional ticket/issue ID</param>
	/// <param name="startTime">Start time for the entry</param>
	/// <param name="endTime">Optional end time (null = active entry)</param>
	/// <param name="description">Optional description of the work</param>
	/// <returns>Result indicating success or failure</returns>
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
	/// <param name="id">ID of the work entry to delete</param>
	/// <returns>Result indicating success or failure</returns>
	Task<Result> DeleteWorkEntryAsync(int id);

	/// <summary>
	/// Refreshes the state from the database.
	/// Useful when external changes have been made to the active work entry.
	/// </summary>
	/// <returns>A task representing the asynchronous operation</returns>
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
	/// This signals that UI components should refresh their work entry lists.
	/// </summary>
	event EventHandler? WorkEntriesModified;

	/// <summary>
	/// Notifies that work entries have been modified by external operations.
	/// This will trigger the WorkEntriesModified event and refresh the active work state.
	/// Call this after any operation that modifies work entries (e.g., after editing/deleting entries).
	/// </summary>
	void NotifyWorkEntriesModified();
}
