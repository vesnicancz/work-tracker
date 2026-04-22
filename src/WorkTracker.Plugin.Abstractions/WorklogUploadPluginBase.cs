using Microsoft.Extensions.Logging;

namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// Base class for worklog upload plugins. Inherits configuration, validation, and lifecycle from <see cref="PluginBase"/>.
/// </summary>
public abstract class WorklogUploadPluginBase(ILogger logger) : PluginBase(logger), IWorklogUploadPlugin
{
	/// <summary>
	/// Submission modes supported by this plugin. Defaults to <see cref="WorklogSubmissionMode.Timed"/>;
	/// plugins that accept pre-aggregated entries should override to include <see cref="WorklogSubmissionMode.Aggregated"/>.
	/// </summary>
	public virtual WorklogSubmissionMode SupportedModes => WorklogSubmissionMode.Timed;

	public abstract Task<PluginResult<bool>> TestConnectionAsync(IProgress<string>? progress, CancellationToken cancellationToken);

	public abstract Task<PluginResult<bool>> UploadWorklogAsync(PluginWorklogEntry worklog, CancellationToken cancellationToken);

	public virtual async Task<PluginResult<WorklogSubmissionResult>> UploadWorklogsAsync(IEnumerable<PluginWorklogEntry> worklogs, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		if (!SupportedModes.HasFlag(mode))
		{
			return PluginResult<WorklogSubmissionResult>.Failure(
				$"Plugin does not support submission mode '{mode}'",
				PluginErrorCategory.Validation);
		}

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
