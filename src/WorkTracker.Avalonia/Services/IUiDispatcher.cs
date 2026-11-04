namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Runs work on the UI thread, for callers that do not start there.
/// </summary>
public interface IUiDispatcher
{
	Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);

	/// <summary>
	/// Queues work on the UI thread without waiting for it.
	/// </summary>
	void Post(Action action);
}
