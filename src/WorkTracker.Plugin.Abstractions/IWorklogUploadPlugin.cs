namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Interface for worklog upload provider plugins
/// </summary>
public interface IWorklogUploadPlugin : IPlugin
{
	/// <summary>
	/// Gets the configuration fields required by this plugin
	/// </summary>
	/// <returns>List of configuration field definitions</returns>
	IReadOnlyList<PluginConfigurationField> GetConfigurationFields();

	/// <summary>
	/// Tests the connection with the current configuration
	/// </summary>
	/// <returns>Result indicating if the connection is successful</returns>
	Task<PluginResult<bool>> TestConnectionAsync();

	/// <summary>
	/// Uploads a single worklog entry
	/// </summary>
	/// <param name="worklog">The worklog to upload</param>
	/// <returns>Result indicating success or failure</returns>
	Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog);

	/// <summary>
	/// Uploads multiple worklog entries in batch
	/// </summary>
	/// <param name="worklogs">The worklogs to upload</param>
	/// <returns>Result with submission summary</returns>
	Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs);

	/// <summary>
	/// Gets existing worklogs from the provider for a specific date range
	/// </summary>
	/// <param name="startDate">Start date</param>
	/// <param name="endDate">End date</param>
	/// <returns>List of existing worklogs</returns>
	Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate);

	/// <summary>
	/// Checks if a worklog already exists in the provider
	/// </summary>
	/// <param name="worklog">The worklog to check</param>
	/// <returns>True if the worklog exists</returns>
	Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog);
}
