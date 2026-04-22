namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Submission shapes that a worklog upload plugin can accept.
/// Timed entries carry StartTime/EndTime as real intervals; Aggregated entries
/// use StartTime as a representative date/time only and rely on DurationMinutes
/// for the total time (no enforced relationship between StartTime and EndTime).
/// Flags form is used by plugins to advertise multiple supported modes; a
/// *selected* mode passed to upload/preview APIs must always be a single value.
/// </summary>
[Flags]
public enum WorklogSubmissionMode
{
	Timed = 1,
	Aggregated = 2
}

public static class WorklogSubmissionModeExtensions
{
	/// <summary>
	/// True only for exactly one of <see cref="WorklogSubmissionMode.Timed"/> or
	/// <see cref="WorklogSubmissionMode.Aggregated"/>. Rejects 0 and combined flags.
	/// </summary>
	public static bool IsSingleMode(this WorklogSubmissionMode mode) =>
		mode is WorklogSubmissionMode.Timed or WorklogSubmissionMode.Aggregated;
}
