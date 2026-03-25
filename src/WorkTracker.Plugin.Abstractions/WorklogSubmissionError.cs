namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Individual worklog submission error
/// </summary>
public class WorklogSubmissionError
{
	public required PluginWorklogEntry Worklog { get; init; }

	public required string ErrorMessage { get; init; }
}
