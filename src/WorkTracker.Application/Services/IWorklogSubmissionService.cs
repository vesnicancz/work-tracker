using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Services;

public interface IWorklogSubmissionService
{
	/// <summary>
	/// Submits worklogs for a single day
	/// </summary>
	Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, CancellationToken cancellationToken = default);

	/// <summary>
	/// Submits worklogs for a single day using specified provider
	/// </summary>
	Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Previews worklogs for a single day
	/// </summary>
	Task<WorklogSubmissionDto> PreviewDailyWorklogAsync(DateTime date, CancellationToken cancellationToken = default);

	/// <summary>
	/// Submits worklogs for an entire week
	/// </summary>
	Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, CancellationToken cancellationToken = default);

	/// <summary>
	/// Submits worklogs for an entire week using specified provider
	/// </summary>
	Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Previews worklogs for an entire week
	/// </summary>
	Task<Dictionary<DateTime, WorklogSubmissionDto>> PreviewWeeklyWorklogAsync(DateTime date, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets all available worklog upload providers
	/// </summary>
	IEnumerable<ProviderInfo> GetAvailableProviders();

	/// <summary>
	/// Submits custom worklogs using specified provider (for edited worklogs).
	/// </summary>
	/// <param name="worklogs">Worklog entries to submit. In <see cref="WorklogSubmissionMode.Aggregated"/>
	/// mode entries are expected to be already grouped by code+description per day with
	/// <c>DurationMinutes</c> holding the total.</param>
	/// <param name="providerId">Target plugin id. Must support <paramref name="mode"/>.</param>
	/// <param name="mode">Submission shape passed through to the plugin.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<Result<SubmissionResult>> SubmitCustomWorklogsAsync(IEnumerable<WorklogDto> worklogs, string providerId, WorklogSubmissionMode mode, CancellationToken cancellationToken = default);
}
