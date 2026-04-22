namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Submission shapes that a worklog upload plugin can accept.
/// Timed entries carry StartTime/EndTime as real intervals; Aggregated entries
/// use StartTime as a representative date/time only and rely on DurationMinutes
/// for the total time (no enforced relationship between StartTime and EndTime).
/// </summary>
[Flags]
public enum WorklogSubmissionMode
{
	Timed = 1,
	Aggregated = 2
}
