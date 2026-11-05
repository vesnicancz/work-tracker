namespace WorkTracker.Application.Common;

/// <summary>
/// Detailed result of worklog submission
/// </summary>
public sealed class SubmissionResult
{
	public int TotalEntries { get; init; }

	public int SuccessfulEntries { get; init; }

	public int FailedEntries { get; init; }

	public IReadOnlyList<SubmissionError> Errors { get; init; } = [];

	/// <summary>
	/// How many things went wrong. A plugin can report a failure no entry is counted for, like GoranG3
	/// failing to submit the timesheet of a day after all its entries went through.
	/// </summary>
	public int FailureCount => Math.Max(FailedEntries, Errors.Count);

	public bool HasFailures => FailureCount > 0;
}
