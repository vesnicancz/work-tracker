namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Tells the user how an action started outside the main window turned out: a toast in the window
/// when they are looking at it, a system notification otherwise.
/// Can be called from any thread and does not throw: a lost message must not fail the action it reports.
/// </summary>
public interface IActionFeedbackService
{
	Task ReportSuccessAsync(string message, CancellationToken cancellationToken);

	Task ReportFailureAsync(string message, CancellationToken cancellationToken);
}
