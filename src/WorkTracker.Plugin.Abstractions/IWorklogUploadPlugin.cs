namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// What can be asked of a worklog upload plugin without starting it.
/// </summary>
public interface IWorklogUploadPluginDescriptor : IPluginDescriptor
{
	/// <summary>
	/// Submission modes this plugin accepts. Defaults to <see cref="WorklogSubmissionMode.Timed"/>
	/// for plugins that only handle per-entry start/end uploads. Plugins that can ingest
	/// aggregated entries (totals per code+name per day) declare <see cref="WorklogSubmissionMode.Aggregated"/>
	/// in addition.
	/// </summary>
	WorklogSubmissionMode SupportedModes { get; }
}

/// <summary>
/// Interface for worklog upload provider plugins
/// </summary>
public interface IWorklogUploadPlugin : ITestablePlugin, IWorklogUploadPluginDescriptor
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
	/// <param name="mode">Submission mode — signals how <c>StartTime</c>/<c>EndTime</c>/<c>DurationMinutes</c>
	/// should be interpreted. In <see cref="WorklogSubmissionMode.Aggregated"/> mode entries are pre-grouped
	/// by code+description per day, <c>StartTime</c> is representative only and <c>DurationMinutes</c> is
	/// the authoritative total.</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Result with submission summary</returns>
	Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, WorklogSubmissionMode mode, CancellationToken cancellationToken = default);
}
