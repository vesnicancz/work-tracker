using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;

namespace WorkTracker.Application.Services;

public interface IWorklogSubmissionService
{
	/// <summary>
	/// Submits worklogs for a single day
	/// </summary>
	Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date);

	/// <summary>
	/// Submits worklogs for a single day using specified provider
	/// </summary>
	Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, string providerId);

	/// <summary>
	/// Previews worklogs for a single day
	/// </summary>
	Task<WorklogSubmissionDto> PreviewDailyWorklogAsync(DateTime date);

	/// <summary>
	/// Submits worklogs for an entire week
	/// </summary>
	Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date);

	/// <summary>
	/// Submits worklogs for an entire week using specified provider
	/// </summary>
	Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, string providerId);

	/// <summary>
	/// Previews worklogs for an entire week
	/// </summary>
	Task<Dictionary<DateTime, WorklogSubmissionDto>> PreviewWeeklyWorklogAsync(DateTime date);

	/// <summary>
	/// Gets all available worklog upload providers
	/// </summary>
	IEnumerable<DTOs.ProviderInfo> GetAvailableProviders();

	/// <summary>
	/// Submits custom worklogs using specified provider (for edited worklogs)
	/// </summary>
	Task<Result<SubmissionResult>> SubmitCustomWorklogsAsync(IEnumerable<Domain.DTOs.WorklogDto> worklogs, string providerId);
}