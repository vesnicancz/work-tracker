namespace WorkTracker.UI.Shared;

public static class TaskExtensions
{
	/// <summary>
	/// Awaits a task and routes any exception to the provided handler
	/// instead of leaving it unobserved.
	/// </summary>
	public static async Task SafeFireAndForgetAsync(this Task task, Action<Exception> onError)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Normal cancellation — not an error worth reporting.
		}
		catch (Exception ex)
		{
			onError(ex);
		}
	}
}
