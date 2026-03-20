namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Result of worklog submission
/// </summary>
public class WorklogSubmissionResult
{
	public int TotalEntries { get; init; }

	public int SuccessfulEntries { get; init; }

	public int FailedEntries { get; init; }

	public IReadOnlyList<WorklogSubmissionError> Errors { get; init; } = Array.Empty<WorklogSubmissionError>();
}
