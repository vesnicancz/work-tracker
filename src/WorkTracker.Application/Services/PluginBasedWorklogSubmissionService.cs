using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;
using WorkTracker.Domain.Entities;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Services;

/// <summary>
/// Service for submitting worklogs using plugin system
/// </summary>
public sealed class PluginBasedWorklogSubmissionService : IWorklogSubmissionService
{
	private static WorklogDto ToWorklogDto(WorkEntry e) => new()
	{
		TicketId = e.TicketId,
		StartTime = e.StartTime,
		EndTime = e.EndTime!.Value,
		Description = e.Description,
		DurationMinutes = Math.Max(1, (int)Math.Ceiling((e.EndTime!.Value - e.StartTime).TotalMinutes))
	};

	private readonly IWorkEntryService _workEntryService;
	private readonly IDateRangeService _dateRangeService;
	private readonly IWorklogValidator _validator;
	private readonly IPluginManager _pluginManager;
	private readonly ILogger<PluginBasedWorklogSubmissionService> _logger;

	public PluginBasedWorklogSubmissionService(
		IWorkEntryService workEntryService,
		IDateRangeService dateRangeService,
		IWorklogValidator validator,
		IPluginManager pluginManager,
		ILogger<PluginBasedWorklogSubmissionService> logger)
	{
		_workEntryService = workEntryService;
		_dateRangeService = dateRangeService;
		_validator = validator;
		_pluginManager = pluginManager;
		_logger = logger;
	}

	public async Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(null);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>("No worklog upload plugin available");
		}

		return await SubmitDailyWorklogAsync(date, plugin.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		_logger.LogInformation("Submitting daily worklog for {Date} using plugin {Plugin}", date.ToShortDateString(), plugin.Metadata.Name);

		var entries = await _workEntryService.GetWorkEntriesByDateAsync(date, cancellationToken);

		return await SubmitWorklogsInternalAsync(plugin, entries, date, cancellationToken);
	}

	public async Task<WorklogSubmissionDto> PreviewDailyWorklogAsync(DateTime date, CancellationToken cancellationToken)
	{
		var entries = await _workEntryService.GetWorkEntriesByDateAsync(date, cancellationToken);
		var completedEntries = entries.Where(e => e.EndTime.HasValue).ToList();

		var dto = new WorklogSubmissionDto
		{
			SubmissionDate = date,
			Worklogs = completedEntries.Select(ToWorklogDto).ToList()
		};

		return dto;
	}

	public async Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(null);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>("No worklog upload plugin available");
		}

		return await SubmitWeeklyWorklogAsync(date, plugin.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		_logger.LogInformation("Submitting weekly worklog for week of {Date} using plugin {Plugin}", date.ToShortDateString(), plugin.Metadata.Name);

		var (weekStart, weekEnd) = _dateRangeService.GetWeekRange(date);
		var entries = await _workEntryService.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, cancellationToken);

		return await SubmitWorklogsInternalAsync(plugin, entries, date, cancellationToken);
	}

	public async Task<Dictionary<DateTime, WorklogSubmissionDto>> PreviewWeeklyWorklogAsync(DateTime date, CancellationToken cancellationToken)
	{
		var (weekStart, weekEnd) = _dateRangeService.GetWeekRange(date);
		var entries = await _workEntryService.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, cancellationToken);
		var completedByDate = entries
			.Where(e => e.EndTime.HasValue)
			.GroupBy(e => e.StartTime.Date)
			.ToDictionary(g => g.Key, g => g.ToList());

		var preview = new Dictionary<DateTime, WorklogSubmissionDto>();

		for (var day = weekStart; day <= weekEnd; day = day.AddDays(1))
		{
			completedByDate.TryGetValue(day, out var dayEntries);
			preview[day] = new WorklogSubmissionDto
			{
				SubmissionDate = day,
				Worklogs = (dayEntries?.Select(ToWorklogDto) ?? []).ToList()
			};
		}

		return preview;
	}

	public IEnumerable<ProviderInfo> GetAvailableProviders()
	{
		return _pluginManager.WorklogUploadPlugins.Select(p => new ProviderInfo
		{
			Id = p.Metadata.Id,
			Name = p.Metadata.Name,
			SupportedModes = p.SupportedModes
		});
	}

	public async Task<Result<SubmissionResult>> SubmitCustomWorklogsAsync(
		IEnumerable<WorklogDto> worklogs,
		string providerId,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		if (!mode.IsSingleMode())
		{
			return Result.Failure<SubmissionResult>($"Invalid submission mode '{mode}' — must be exactly Timed or Aggregated");
		}

		var plugin = ResolvePlugin(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		if (!plugin.SupportedModes.HasFlag(mode))
		{
			return Result.Failure<SubmissionResult>($"Plugin '{plugin.Metadata.Name}' does not support submission mode '{mode}'");
		}

		var worklogList = worklogs.ToList();

		_logger.LogInformation("Submitting {Count} custom worklogs using plugin {Plugin} (mode={Mode})",
			worklogList.Count, plugin.Metadata.Name, mode);

		return await ValidateAndUploadAsync(plugin, worklogList, mode, cancellationToken);
	}

	private IWorklogUploadPlugin? ResolvePlugin(string? providerId)
	{
		if (providerId == null)
		{
			return _pluginManager.WorklogUploadPlugins.FirstOrDefault();
		}

		return _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
	}

	private async Task<Result<SubmissionResult>> SubmitWorklogsInternalAsync(
		IWorklogUploadPlugin plugin,
		IEnumerable<WorkEntry> entries,
		DateTime date,
		CancellationToken cancellationToken)
	{
		var completedEntries = entries.Where(e => e.EndTime.HasValue).ToList();

		if (completedEntries.Count == 0)
		{
			_logger.LogWarning("No completed entries found for the specified period (reference date: {Date})", date.ToShortDateString());
			return Result.Success(new SubmissionResult
			{
				TotalEntries = 0,
				SuccessfulEntries = 0,
				FailedEntries = 0
			});
		}

		var worklogs = completedEntries.Select(ToWorklogDto).ToList();
		var result = await ValidateAndUploadAsync(plugin, worklogs, WorklogSubmissionMode.Timed, cancellationToken);

		if (result.IsSuccess)
		{
			_logger.LogInformation("Submitted {Successful}/{Total} worklogs successfully", result.Value!.SuccessfulEntries, result.Value!.TotalEntries);
		}

		return result;
	}

	/// <summary>
	/// The one way worklogs reach a plugin. Invalid ones stay out of the upload and come back as failed
	/// entries, so the caller can show the reason instead of them silently going missing.
	/// </summary>
	private async Task<Result<SubmissionResult>> ValidateAndUploadAsync(
		IWorklogUploadPlugin plugin,
		List<WorklogDto> worklogs,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		var valid = new List<WorklogDto>();
		var rejected = new List<SubmissionError>();

		foreach (var worklog in worklogs)
		{
			var validation = _validator.Validate(worklog);
			if (validation.IsValid)
			{
				valid.Add(worklog);
			}
			else
			{
				var reason = string.Join(", ", validation.Errors);
				_logger.LogWarning("Not submitting invalid worklog: {Errors}", reason);
				rejected.Add(ToSubmissionError(worklog.TicketId, worklog.Description, worklog.StartTime, worklog.EndTime, reason));
			}
		}

		if (valid.Count == 0 && rejected.Count != 0)
		{
			return Result.Success(new SubmissionResult
			{
				TotalEntries = rejected.Count,
				FailedEntries = rejected.Count,
				Errors = rejected
			});
		}

		var result = await UploadAndMapResultAsync(plugin, valid, mode, cancellationToken);
		if (result.IsFailure || rejected.Count == 0)
		{
			return result;
		}

		var uploaded = result.Value!;
		return Result.Success(new SubmissionResult
		{
			TotalEntries = uploaded.TotalEntries + rejected.Count,
			SuccessfulEntries = uploaded.SuccessfulEntries,
			FailedEntries = uploaded.FailedEntries + rejected.Count,
			Errors = [.. uploaded.Errors, .. rejected]
		});
	}

	private async Task<Result<SubmissionResult>> UploadAndMapResultAsync(
		IWorklogUploadPlugin plugin,
		List<WorklogDto> worklogs,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		var result = await plugin.UploadWorklogsAsync(worklogs.ToPluginWorklogs(), mode, cancellationToken);

		if (result.IsFailure)
		{
			return Result.Failure<SubmissionResult>(result.Error!);
		}

		var pluginResult = result.Value!;
		return Result.Success(new SubmissionResult
		{
			TotalEntries = pluginResult.TotalEntries,
			SuccessfulEntries = pluginResult.SuccessfulEntries,
			FailedEntries = pluginResult.FailedEntries,
			Errors = MapPluginErrors(pluginResult.Errors)
		});
	}

	private static List<SubmissionError> MapPluginErrors(IReadOnlyList<WorklogSubmissionError> pluginErrors)
	{
		return pluginErrors
			.Select(e => ToSubmissionError(e.Worklog.TicketId, e.Worklog.Description, e.Worklog.StartTime, e.Worklog.EndTime, e.ErrorMessage))
			.ToList();
	}

	private static SubmissionError ToSubmissionError(string? ticketId, string? description, DateTime startTime, DateTime endTime, string errorMessage) => new()
	{
		TicketId = ticketId ?? string.Empty,
		Date = startTime.Date,
		Description = description,
		ErrorMessage = errorMessage,
		Details = $"{startTime:HH:mm}-{endTime:HH:mm}"
	};
}
