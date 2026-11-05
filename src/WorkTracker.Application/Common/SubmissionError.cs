namespace WorkTracker.Application.Common;

public sealed class SubmissionError
{
	public required string ErrorMessage { get; init; }

	/// <summary>
	/// Position of the failed worklog in the list that was submitted, or null when the plugin reported
	/// a position that isn't in it.
	/// </summary>
	public int? WorklogIndex { get; init; }

	/// <summary>
	/// The day of the failed worklog, known whenever <see cref="WorklogIndex"/> is.
	/// </summary>
	public DateTime? Date { get; init; }
}
