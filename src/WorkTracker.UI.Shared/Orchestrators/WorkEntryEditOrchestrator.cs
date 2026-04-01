using System.Text;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Orchestrators;

public class WorkEntryEditOrchestrator : IWorkEntryEditOrchestrator
{
	private readonly IWorklogStateService _worklogStateService;
	private readonly IDialogService _dialogService;
	private readonly ILocalizationService _localization;
	private readonly ILogger<WorkEntryEditOrchestrator> _logger;

	public WorkEntryEditOrchestrator(
		IWorklogStateService worklogStateService,
		IDialogService dialogService,
		ILocalizationService localization,
		ILogger<WorkEntryEditOrchestrator> logger)
	{
		_worklogStateService = worklogStateService;
		_dialogService = dialogService;
		_localization = localization;
		_logger = logger;
	}

	public string? Validate(string? ticketId, string? description, bool hasEndTime, DateTime startDateTime, DateTime? endDateTime)
	{
		if (string.IsNullOrWhiteSpace(ticketId) && string.IsNullOrWhiteSpace(description))
		{
			return _localization["EitherTicketOrDescriptionRequired"];
		}

		if (hasEndTime && endDateTime.HasValue && endDateTime.Value <= startDateTime)
		{
			return _localization["EndTimeMustBeAfterStartTime"];
		}

		return null;
	}

	public Task<Result<bool>> SaveNewAsync(string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken)
	{
		return SaveWithOverlapHandlingAsync(
			excludeEntryId: null,
			startDateTime,
			endDateTime,
			async (plan, ct) => (Result)await _worklogStateService.CreateWorkEntryWithResolutionAsync(ticketId, startDateTime, description, endDateTime, plan, ct),
			async ct => (Result)await _worklogStateService.CreateWorkEntryAsync(ticketId, startDateTime, description, endDateTime, ct),
			cancellationToken);
	}

	public Task<Result<bool>> SaveExistingAsync(int entryId, string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken)
	{
		return SaveWithOverlapHandlingAsync(
			excludeEntryId: entryId,
			startDateTime,
			endDateTime,
			(plan, ct) => _worklogStateService.UpdateWorkEntryWithResolutionAsync(entryId, ticketId, startDateTime, endDateTime, description, plan, ct),
			ct => _worklogStateService.UpdateWorkEntryAsync(entryId, ticketId, startDateTime, endDateTime, description, ct),
			cancellationToken);
	}

	private async Task<Result<bool>> SaveWithOverlapHandlingAsync(
		int? excludeEntryId,
		DateTime startDateTime,
		DateTime? endDateTime,
		Func<OverlapResolutionPlan, CancellationToken, Task<Result>> withResolution,
		Func<CancellationToken, Task<Result>> withoutResolution,
		CancellationToken cancellationToken)
	{
		var plan = await _worklogStateService.ComputeOverlapResolutionAsync(excludeEntryId, startDateTime, endDateTime, cancellationToken);

		if (plan.HasAdjustments)
		{
			if (!plan.IsOnlyClosingActiveEntry)
			{
				var message = BuildOverlapMessage(plan);
				var confirmed = await _dialogService.ShowConfirmationAsync(message, _localization["OverlapResolutionTitle"]);

				if (!confirmed)
				{
					return Result.Success(false);
				}
			}

			var result = await withResolution(plan, cancellationToken);
			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to save work entry with resolution: {Error}", result.Error);
				return Result.Failure<bool>(result.Error);
			}

			return Result.Success(true);
		}

		var saveResult = await withoutResolution(cancellationToken);
		if (saveResult.IsFailure)
		{
			_logger.LogWarning("Failed to save work entry: {Error}", saveResult.Error);
			return Result.Failure<bool>(saveResult.Error);
		}

		return Result.Success(true);
	}

	private string BuildOverlapMessage(OverlapResolutionPlan plan)
	{
		var sb = new StringBuilder();
		sb.AppendLine(_localization["OverlapResolutionMessage"]);
		sb.AppendLine();

		foreach (var adjustment in plan.Adjustments)
		{
			var entryLabel = !string.IsNullOrWhiteSpace(adjustment.TicketId)
				? adjustment.TicketId
				: adjustment.Description ?? "?";

			switch (adjustment.Kind)
			{
				case OverlapAdjustmentKind.TrimEnd:
					sb.AppendLine(_localization.GetFormattedString("OverlapTrimEnd",
						entryLabel,
						adjustment.OriginalEnd?.ToString("HH:mm") ?? "∞",
						adjustment.NewEnd?.ToString("HH:mm") ?? "?"));
					break;

				case OverlapAdjustmentKind.TrimStart:
					sb.AppendLine(_localization.GetFormattedString("OverlapTrimStart",
						entryLabel,
						adjustment.OriginalStart.ToString("HH:mm"),
						adjustment.NewStart?.ToString("HH:mm") ?? "?"));
					break;

				case OverlapAdjustmentKind.Delete:
					sb.AppendLine(_localization.GetFormattedString("OverlapDelete", entryLabel));
					break;

				case OverlapAdjustmentKind.Split:
					sb.AppendLine(_localization.GetFormattedString("OverlapSplit", entryLabel));
					break;
			}
		}

		return sb.ToString().TrimEnd();
	}
}
