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
			Name = p.Metadata.Name
		});
	}

	public async Task<Result<SubmissionResult>> SubmitCustomWorklogsAsync(
		IEnumerable<WorklogDto> worklogs,
		string providerId,
		CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		var worklogList = worklogs.ToList();

		_logger.LogInformation("Submitting {Count} custom worklogs using plugin {Plugin}", worklogList.Count, plugin.Metadata.Name);

		return await UploadAndMapResultAsync(plugin, worklogList, cancellationToken);
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
			_logger.LogWarning("No completed entries found for {Date}", date.ToShortDateString());
			return Result.Success(new SubmissionResult
			{
				TotalEntries = 0,
				SuccessfulEntries = 0,
				FailedEntries = 0
			});
		}

		var validWorklogs = new List<WorklogDto>();
		foreach (var entry in completedEntries)
		{
			var worklog = ToWorklogDto(entry);
			var validationResult = _validator.Validate(worklog);
			if (validationResult.IsValid)
			{
				validWorklogs.Add(worklog);
			}
			else
			{
				_logger.LogWarning("Skipping invalid worklog: {Errors}", string.Join(", ", validationResult.Errors));
			}
		}

		if (validWorklogs.Count == 0)
		{
			return Result.Failure<SubmissionResult>("No valid worklogs to submit");
		}

		var result = await UploadAndMapResultAsync(plugin, validWorklogs, cancellationToken);

		if (result.IsSuccess)
		{
			_logger.LogInformation("Submitted {Successful}/{Total} worklogs successfully", result.Value!.SuccessfulEntries, result.Value!.TotalEntries);
		}

		return result;
	}

	private async Task<Result<SubmissionResult>> UploadAndMapResultAsync(
		IWorklogUploadPlugin plugin,
		List<WorklogDto> worklogs,
		CancellationToken cancellationToken)
	{
		var result = await plugin.UploadWorklogsAsync(worklogs.ToPluginWorklogs(), cancellationToken);

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
		return pluginErrors.Select(e => new SubmissionError
		{
			TicketId = e.Worklog.TicketId ?? string.Empty,
			Date = e.Worklog.StartTime.Date,
			ErrorMessage = e.ErrorMessage,
			Details = $"{e.Worklog.StartTime:HH:mm}-{e.Worklog.EndTime:HH:mm}"
		}).ToList();
	}
}
