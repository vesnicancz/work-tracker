using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Orchestrators;

public class WorklogSubmissionOrchestrator : IWorklogSubmissionOrchestrator
{
	private readonly IWorklogSubmissionService _submissionService;
	private readonly ILocalizationService _localization;
	private readonly ILogger<WorklogSubmissionOrchestrator> _logger;

	public WorklogSubmissionOrchestrator(
		IWorklogSubmissionService submissionService,
		ILocalizationService localization,
		ILogger<WorklogSubmissionOrchestrator> logger)
	{
		_submissionService = submissionService;
		_localization = localization;
		_logger = logger;
	}

	public List<ProviderInfo> LoadAvailableProviders()
	{
		try
		{
			return _submissionService.GetAvailableProviders().ToList();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load available providers");
			return [];
		}
	}

	public Task<IReadOnlyList<WorklogSubmissionDto>> LoadPreviewAsync(DateTime date, bool isWeekly, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		var period = isWeekly ? SubmissionPeriod.WeekOf(date) : SubmissionPeriod.Day(date);
		return _submissionService.PreviewAsync(period, mode, cancellationToken);
	}

	public async Task<SubmissionOutcome> SubmitAsync(
		IReadOnlyList<WorklogDto> worklogs, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, providerId, mode, cancellationToken);

		if (result.IsSuccess && result.Value != null)
		{
			return new SubmissionOutcome(true, FormatSubmissionStatus(result.Value, providerName), result.Value.Errors);
		}

		return new SubmissionOutcome(false, _localization.GetFormattedString("ErrorPrefix", result.Error), []);
	}

	internal string FormatSubmissionStatus(SubmissionResult submission, string providerName)
	{
		if (!submission.HasFailures)
		{
			return _localization.GetFormattedString("SubmissionSuccess", submission.SuccessfulEntries, providerName);
		}

		if (submission.SuccessfulEntries == 0)
		{
			return _localization.GetFormattedString("SubmissionAllFailed", submission.TotalEntries, providerName);
		}

		return _localization.GetFormattedString("SubmissionPartial", submission.SuccessfulEntries, providerName, submission.FailureCount);
	}
}
