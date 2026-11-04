namespace WorkTracker.Application.Common;

/// <summary>
/// Failures of work entry operations that a user runs into. The code lets a UI show them in its own
/// language; the message is what the CLI and the log use.
/// </summary>
public static class WorkEntryErrors
{
	public static readonly ResultError InvalidEntry = new("InvalidEntry", "Invalid work entry data. Both ticket ID and description cannot be empty.");

	public static readonly ResultError InvalidEntryAfterUpdate = new("InvalidEntryAfterUpdate", "Invalid work entry data after update. Both ticket ID and description cannot be empty, and end time cannot be before start time.");

	public static readonly ResultError Overlap = new("Overlap", "This work entry overlaps with an existing entry. Please check your times.");

	public static readonly ResultError PlanOutdated = new("PlanOutdated", "Other work entries changed in the meantime. Nothing was saved, please try again.");

	public static readonly ResultError NoActiveWork = new("NoActiveWork", "No active work entry found to stop");

	public static readonly ResultError InvalidEndTime = new("InvalidEndTime", "Invalid end time - cannot be before start time");

	public static ResultError NotFound(int id) => new("NotFound", $"Work entry with ID {id} not found", id);
}
