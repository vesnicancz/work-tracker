namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Individual worklog submission error
/// </summary>
public class WorklogSubmissionError
{
	/// <summary>
	/// Position of the failed worklog in the list passed to <see cref="IWorklogUploadPlugin.UploadWorklogsAsync"/>.
	/// This is how the failure finds its way back to the row the user sees: two worklogs can look exactly
	/// alike, so their values couldn't tell them apart.
	/// </summary>
	public required int WorklogIndex { get; init; }

	public required string ErrorMessage { get; init; }
}
