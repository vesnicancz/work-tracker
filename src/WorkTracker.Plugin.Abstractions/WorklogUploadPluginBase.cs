namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for worklog upload plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class WorklogUploadPluginBase : PluginBase, IWorklogUploadPlugin
{
	public abstract Task<PluginResult<bool>> TestConnectionAsync(CancellationToken cancellationToken);

	public abstract Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);

	public virtual async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, CancellationToken cancellationToken)
	{
		if (!IsInitialized)
		{
			return PluginResult<WorklogSubmissionResult>.Failure("Plugin is not initialized");
		}

		var worklogList = worklogs.ToList();
		var successful = 0;
		var failed = 0;
		var errors = new List<WorklogSubmissionError>();

		foreach (var worklog in worklogList)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var result = await UploadWorklogAsync(worklog, cancellationToken);
			if (result.IsSuccess)
			{
				successful++;
			}
			else
			{
				failed++;
				errors.Add(new WorklogSubmissionError
				{
					Worklog = worklog,
					ErrorMessage = result.Error ?? "Unknown error"
				});
			}
		}

		var submissionResult = new WorklogSubmissionResult
		{
			TotalEntries = worklogList.Count,
			SuccessfulEntries = successful,
			FailedEntries = failed,
			Errors = errors
		};

		return PluginResult<WorklogSubmissionResult>.Success(submissionResult);
	}

	public abstract Task<PluginResult<IEnumerable<PluginWorklogEntry>>> GetWorklogsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

	public abstract Task<PluginResult<bool>> WorklogExistsAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);
}
