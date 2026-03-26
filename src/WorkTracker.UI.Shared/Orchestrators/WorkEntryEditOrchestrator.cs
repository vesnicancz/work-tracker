using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Orchestrators;

public class WorkEntryEditOrchestrator : IWorkEntryEditOrchestrator
{
	private readonly IWorklogStateService _worklogStateService;
	private readonly ILocalizationService _localization;
	private readonly ILogger<WorkEntryEditOrchestrator> _logger;

	public WorkEntryEditOrchestrator(
		IWorklogStateService worklogStateService,
		ILocalizationService localization,
		ILogger<WorkEntryEditOrchestrator> logger)
	{
		_worklogStateService = worklogStateService;
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

	public async Task<Result> SaveNewAsync(string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken)
	{
		var result = await _worklogStateService.CreateWorkEntryAsync(ticketId, startDateTime, description, endDateTime, cancellationToken);
		if (result.IsFailure)
		{
			_logger.LogWarning("Failed to create work entry: {Error}", result.Error);
		}
		return result;
	}

	public async Task<Result> SaveExistingAsync(int entryId, string? ticketId, DateTime startDateTime, DateTime? endDateTime, string? description, CancellationToken cancellationToken)
	{
		var result = await _worklogStateService.UpdateWorkEntryAsync(entryId, ticketId, startDateTime, endDateTime, description, cancellationToken);
		if (result.IsFailure)
		{
			_logger.LogWarning("Failed to update work entry: {Error}", result.Error);
		}
		return result;
	}
}