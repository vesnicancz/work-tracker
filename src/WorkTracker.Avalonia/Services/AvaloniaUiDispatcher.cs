using Avalonia.Threading;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
	/// <remarks>
	/// The token only ends the wait, which is what keeps shutdown moving: once the main loop has
	/// exited, the dispatcher never runs what was posted to it.
	/// </remarks>
	public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
	{
		return Dispatcher.UIThread.InvokeAsync(action).WaitAsync(cancellationToken);
	}

	public void Post(Action action)
	{
		Dispatcher.UIThread.Post(action);
	}
}
