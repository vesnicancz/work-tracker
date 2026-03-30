namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Interface for worklog upload provider plugins
/// </summary>
public interface IWorklogUploadPlugin : ITestablePlugin
{
	/// <summary>
	/// Uploads a single worklog entry
	/// </summary>
	/// <param name="worklog">The worklog to upload</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Result indicating success or failure</returns>
	Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken = default);

	/// <summary>
	/// Uploads multiple worklog entries in batch
	/// </summary>
	/// <param name="worklogs">The worklogs to upload</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Result with submission summary</returns>
	Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets existing worklogs from the provider for a specific date range
	/// </summary>
	/// <param name="startDate">Start date</param>
	/// <param name="endDate">End date</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>List of existing worklogs</returns>
	Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

	/// <summary>
	/// Checks if a worklog already exists in the provider
	/// </summary>
	/// <param name="worklog">The worklog to check</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if the worklog exists</returns>
	Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken = default);
}
