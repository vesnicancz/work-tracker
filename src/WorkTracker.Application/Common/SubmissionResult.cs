namespace WorkTracker.Application.Common;

/// <summary>
/// Detailed result of worklog submission
/// </summary>
public class SubmissionResult
{
	public int TotalEntries { get; set; }

	public int SuccessfulEntries { get; set; }

	public int FailedEntries { get; set; }

	public List<SubmissionError> Errors { get; set; } = new();

	public Dictionary<DateTime, int> EntriesByDate { get; set; } = new();

	public bool IsSuccess => FailedEntries == 0 && TotalEntries > 0;

	public bool HasPartialSuccess => SuccessfulEntries > 0 && FailedEntries > 0;
}