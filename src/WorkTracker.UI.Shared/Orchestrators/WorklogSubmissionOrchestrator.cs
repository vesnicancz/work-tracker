using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Services;
using WorkTracker.UI.Shared.Helpers;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

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

	public async Task<PreviewLoadResult> LoadPreviewAsync(DateTime date, bool isWeekly, string noTicketLabel, CancellationToken cancellationToken)
	{
		if (isWeekly)
		{
			var weeklyPreview = await _submissionService.PreviewWeeklyWorklogAsync(date, cancellationToken);
			var items = new List<WorklogPreviewItem>();

			foreach (var dayPreview in weeklyPreview.OrderBy(kvp => kvp.Key))
			{
				items.Add(new WorklogPreviewItem
				{
					Date = dayPreview.Key,
					IsDateHeader = true,
					DateDisplay = dayPreview.Key.ToString("dddd, MMMM dd, yyyy")
				});

				foreach (var entry in dayPreview.Value.Worklogs)
				{
					var item = new WorklogPreviewItem
					{
						Date = dayPreview.Key,
						TicketId = entry.TicketId,
						NoTicketLabel = noTicketLabel,
						Description = entry.Description ?? string.Empty,
						Duration = entry.DurationMinutes * 60,
						StartTime = entry.StartTime,
						EndTime = entry.EndTime
					};
					item.SaveOriginalValues();
					items.Add(item);
				}
			}

			var totalSeconds = weeklyPreview.Sum(kvp => kvp.Value.Worklogs.Sum(w => w.DurationMinutes * 60));
			var dataItemCount = items.Count(i => !i.IsDateHeader);
			return new PreviewLoadResult(items, totalSeconds, dataItemCount);
		}
		else
		{
			var dailyPreview = await _submissionService.PreviewDailyWorklogAsync(date, cancellationToken);
			var items = dailyPreview.Worklogs.Select(entry =>
			{
				var item = new WorklogPreviewItem
				{
					Date = date,
					TicketId = entry.TicketId,
					NoTicketLabel = noTicketLabel,
					Description = entry.Description ?? string.Empty,
					Duration = entry.DurationMinutes * 60,
					StartTime = entry.StartTime,
					EndTime = entry.EndTime
				};
				item.SaveOriginalValues();
				return item;
			}).ToList();

			var totalSeconds = dailyPreview.Worklogs.Sum(w => w.DurationMinutes * 60);
			return new PreviewLoadResult(items, totalSeconds, items.Count);
		}
	}

	public async Task<SubmissionOutcome> SubmitAsync(
		IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, CancellationToken cancellationToken)
	{
		var worklogs = ConvertToWorklogs(items.Where(i => !i.IsDateHeader));

		var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, providerId, cancellationToken);

		if (result.IsSuccess && result.Value != null)
		{
			var submission = result.Value;
			var hasFailedItems = MarkFailedItems(items, submission);
			var statusMessage = FormatSubmissionStatus(submission, providerName);
			return new SubmissionOutcome(submission.FailedEntries == 0, hasFailedItems, statusMessage);
		}

		var hasExistingFailedItems = items.Any(i => !i.IsDateHeader && i.HasError);
		return new SubmissionOutcome(false, hasExistingFailedItems,
			_localization.GetFormattedString("ErrorPrefix", result.Error));
	}

	public async Task<SubmissionOutcome> RetryFailedAsync(
		IReadOnlyList<WorklogPreviewItem> items, string providerId, string providerName, CancellationToken cancellationToken)
	{
		var worklogs = ConvertToWorklogs(items.Where(i => !i.IsDateHeader && i.HasError));

		if (worklogs.Count == 0)
		{
			return new SubmissionOutcome(true, false, string.Empty);
		}

		var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, providerId, cancellationToken);

		if (result.IsSuccess && result.Value != null)
		{
			var submission = result.Value;
			var hasFailedItems = MarkFailedItems(items, submission);
			var statusMessage = FormatSubmissionStatus(submission, providerName);
			return new SubmissionOutcome(!hasFailedItems, hasFailedItems, statusMessage);
		}

		return new SubmissionOutcome(false, true,
			_localization.GetFormattedString("ErrorPrefix", result.Error));
	}

	internal bool MarkFailedItems(IReadOnlyList<WorklogPreviewItem> items, SubmissionResult submission)
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
			var match = dataItems.FirstOrDefault(i =>
				i.Date.Date == error.Date.Date &&
				(string.IsNullOrEmpty(error.TicketId) || i.TicketId == error.TicketId) &&
				error.Details == $"{i.StartTime:HH:mm}-{i.EndTime:HH:mm}");

			// Fallback: match by TicketId + Date only (ignore TicketId when error has none)
			match ??= dataItems.FirstOrDefault(i =>
				i.Date.Date == error.Date.Date &&
				(string.IsNullOrEmpty(error.TicketId) || i.TicketId == error.TicketId) &&
				!i.HasError);

			if (match != null)
			{
				match.HasError = true;
				match.ErrorMessage = error.ErrorMessage;
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

	public string FormatDuration(int seconds) => DurationFormatter.Format(seconds);

	internal string FormatSubmissionStatus(SubmissionResult submission, string providerName)
	{
		if (submission.FailedEntries == 0)
		{
			return _localization.GetFormattedString("SubmissionSuccess", submission.SuccessfulEntries, providerName);
		}

		if (submission.SuccessfulEntries == 0)
		{
			return _localization.GetFormattedString("SubmissionAllFailed", submission.TotalEntries, providerName);
		}

		return _localization.GetFormattedString("SubmissionPartial", submission.SuccessfulEntries, providerName, submission.FailedEntries);
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