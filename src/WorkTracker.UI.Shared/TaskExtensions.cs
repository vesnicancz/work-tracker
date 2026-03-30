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
			await task;
		}
		catch (Exception ex)
		{
			onError(ex);
		}
	}
}
