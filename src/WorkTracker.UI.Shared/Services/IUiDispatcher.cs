namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Runs work on the UI thread, for callers that do not start there.
/// </summary>
public interface IUiDispatcher
{
	Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}
