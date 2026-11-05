using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Services;

public interface IWorklogSubmissionService
{
	/// <summary>
	/// The completed work of each day of <paramref name="period"/>, in order, empty days included.
	/// </summary>
	/// <param name="period">The days to preview.</param>
	/// <param name="mode">In <see cref="WorklogSubmissionMode.Aggregated"/> mode the entries of a day are
	/// grouped by ticket and description, with <c>DurationMinutes</c> holding the total, the earliest start
	/// as <c>StartTime</c> and no real interval.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<IReadOnlyList<WorklogSubmissionDto>> PreviewAsync(SubmissionPeriod period, WorklogSubmissionMode mode, CancellationToken cancellationToken);

	/// <summary>
	/// Submits the completed entries of <paramref name="period"/> as they are recorded.
	/// </summary>
	/// <param name="period">The days to submit.</param>
	/// <param name="providerId">Target plugin id, or null to use the only enabled one. With several enabled
	/// null fails, because worklogs in the wrong system can't be taken back.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<Result<SubmissionResult>> SubmitAsync(SubmissionPeriod period, string? providerId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets all available worklog upload providers
	/// </summary>
	IEnumerable<ProviderInfo> GetAvailableProviders();

	/// <summary>
	/// Submits custom worklogs using specified provider (for edited worklogs).
	/// </summary>
	/// <param name="worklogs">Worklog entries to submit, already shaped for <paramref name="mode"/> the way
	/// <see cref="PreviewAsync"/> returns them.</param>
	/// <param name="providerId">Target plugin id. Must support <paramref name="mode"/>.</param>
	/// <param name="mode">Submission shape passed through to the plugin.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task<Result<SubmissionResult>> SubmitCustomWorklogsAsync(IEnumerable<WorklogDto> worklogs, string providerId, WorklogSubmissionMode mode, CancellationToken cancellationToken);
}
