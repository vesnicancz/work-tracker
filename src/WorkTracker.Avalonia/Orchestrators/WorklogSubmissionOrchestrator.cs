using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Helpers;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;

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

	public async Task<PreviewLoadResult> LoadPreviewAsync(DateTime date, bool isWeekly, WorklogSubmissionMode mode, string noTicketLabel, CancellationToken cancellationToken)
	{
		var period = isWeekly ? SubmissionPeriod.WeekOf(date) : SubmissionPeriod.Day(date);
		var days = await _submissionService.PreviewAsync(period, mode, cancellationToken);
		var items = new List<WorklogPreviewItem>();
		var dataItems = new List<WorklogPreviewItem>();

		foreach (var day in days)
		{
			if (isWeekly)
			{
				items.Add(new WorklogPreviewItem
				{
					Date = day.SubmissionDate,
					IsDateHeader = true,
					DateDisplay = day.SubmissionDate.ToString("dddd, MMMM dd, yyyy")
				});
			}

			foreach (var worklog in day.Worklogs)
			{
				var item = new WorklogPreviewItem
				{
					Date = day.SubmissionDate,
					IsAggregated = mode == WorklogSubmissionMode.Aggregated,
					TicketId = worklog.TicketId,
					NoTicketLabel = noTicketLabel,
					Description = worklog.Description ?? string.Empty,
					Duration = worklog.DurationMinutes * 60,
					StartTime = worklog.StartTime,
					EndTime = worklog.EndTime
				};
				item.SaveOriginalValues();
				items.Add(item);
				dataItems.Add(item);
			}
		}

		return new PreviewLoadResult(items, dataItems.Sum(i => i.Duration), dataItems.Count);
	}

	public async Task<SubmissionOutcome> SubmitAsync(
		IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		var submittedItems = items.Where(i => !i.IsDateHeader && i.IsSelected).ToList();
		var worklogs = ConvertToWorklogs(submittedItems);

		var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, providerId, mode, cancellationToken);

		if (result.IsSuccess && result.Value != null)
		{
			var submission = result.Value;
			var hasFailedItems = MarkFailedItems(items, submittedItems, submission);
			var statusMessage = FormatSubmissionStatus(submission, providerName);
			return new SubmissionOutcome(!submission.HasFailures, hasFailedItems, statusMessage);
		}

		var hasExistingFailedItems = items.Any(i => !i.IsDateHeader && i.HasError);
		return new SubmissionOutcome(false, hasExistingFailedItems,
			_localization.GetFormattedString("ErrorPrefix", result.Error));
	}

	public async Task<SubmissionOutcome> RetryFailedAsync(
		IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		var attemptedItems = items.Where(i => !i.IsDateHeader && i.HasError && i.IsSelected).ToList();
		var worklogs = ConvertToWorklogs(attemptedItems);

		if (worklogs.Count == 0)
		{
			return new SubmissionOutcome(true, false, string.Empty);
		}

		var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, providerId, mode, cancellationToken);

		if (result.IsSuccess && result.Value != null)
		{
			var submission = result.Value;
			MarkFailedItems(attemptedItems, attemptedItems, submission);
			var hasFailedItems = items.Any(i => !i.IsDateHeader && i.HasError);
			var statusMessage = FormatSubmissionStatus(submission, providerName);
			return new SubmissionOutcome(!submission.HasFailures && !hasFailedItems, hasFailedItems, statusMessage);
		}

		return new SubmissionOutcome(false, true,
			_localization.GetFormattedString("ErrorPrefix", result.Error));
	}

	/// <summary>
	/// Clears the error state of <paramref name="items"/> and marks the rows that failed.
	/// </summary>
	/// <param name="items">The rows whose previous error state no longer applies.</param>
	/// <param name="submittedItems">The rows that were submitted, in the order they were sent.</param>
	/// <param name="submission">The result of the submission.</param>
	internal bool MarkFailedItems(IReadOnlyList<WorklogPreviewItem> items, IReadOnlyList<WorklogPreviewItem> submittedItems, SubmissionResult submission)
	{
		var dataItems = items.Where(i => !i.IsDateHeader).ToList();

		// Clear previous error state
		foreach (var item in dataItems)
		{
			item.HasError = false;
			item.ErrorMessage = null;
		}

		if (submission.Errors.Count == 0)
		{
			return false;
		}

		foreach (var error in submission.Errors)
		{
			// Without a position the plugin broke its contract. Guessing a row could mark one that went
			// through; the failure still counts in the status message and the service logged it.
			if (error.WorklogIndex is { } index)
			{
				submittedItems[index].HasError = true;
				submittedItems[index].ErrorMessage = error.ErrorMessage;
			}
		}

		return dataItems.Any(i => i.HasError);
	}

	public void ResetItems(IReadOnlyList<WorklogPreviewItem> items)
	{
		foreach (var item in items.Where(i => !i.IsDateHeader))
		{
			item.RestoreOriginalValues();
			item.HasError = false;
			item.ErrorMessage = null;
		}
	}

	public void InvertSelection(IReadOnlyList<WorklogPreviewItem> items)
	{
		foreach (var item in items.Where(i => !i.IsDateHeader))
		{
			item.IsSelected = !item.IsSelected;
		}
	}

	public void SelectAll(IReadOnlyList<WorklogPreviewItem> items)
	{
		foreach (var item in items.Where(i => !i.IsDateHeader))
		{
			item.IsSelected = true;
		}
	}

	public string FormatDuration(int seconds) => DurationFormatter.Format(seconds);

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

	private static List<WorklogDto> ConvertToWorklogs(IEnumerable<WorklogPreviewItem> items)
	{
		return items.Select(i => new WorklogDto
		{
			TicketId = i.TicketId,
			Description = i.Description,
			StartTime = i.StartTime,
			EndTime = i.EndTime,
			DurationMinutes = i.Duration / 60
		}).ToList();
	}
}
