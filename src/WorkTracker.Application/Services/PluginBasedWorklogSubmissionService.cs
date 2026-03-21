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
		var defaultPlugin = _pluginManager.WorklogUploadPlugins.FirstOrDefault();
		if (defaultPlugin == null)
		{
			return Result.Failure<SubmissionResult>("No worklog upload plugin available");
		}

		return await SubmitDailyWorklogAsync(date, defaultPlugin.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		_logger.LogInformation("Submitting daily worklog for {Date} using plugin {Plugin}", date.ToShortDateString(), plugin.Metadata.Name);

		var entries = await _workEntryService.GetWorkEntriesByDateAsync(date, cancellationToken);
		var completedEntries = entries.Where(e => e.EndTime.HasValue).ToList();

		if (completedEntries.Count == 0)
		{
			_logger.LogWarning("No completed entries found for {Date}", date);
			return Result.Success(new SubmissionResult
			{
				TotalEntries = 0,
				SuccessfulEntries = 0,
				FailedEntries = 0
			});
		}

		var worklogs = completedEntries.Select(ToWorklogDto).ToList();

		// Validate worklogs
		var validWorklogs = new List<WorklogDto>();
		foreach (var worklog in worklogs)
		{
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

		// Submit using plugin (convert to plugin types)
		var result = await plugin.UploadWorklogsAsync(validWorklogs.ToPluginWorklogs(), cancellationToken);

		if (result.IsFailure)
		{
			return Result.Failure<SubmissionResult>(result.Error!);
		}

		var pluginResult = result.Value!;
		var submissionResult = new SubmissionResult
		{
			TotalEntries = pluginResult.TotalEntries,
			SuccessfulEntries = pluginResult.SuccessfulEntries,
			FailedEntries = pluginResult.FailedEntries
		};

		_logger.LogInformation("Submitted {Successful}/{Total} worklogs successfully", submissionResult.SuccessfulEntries, submissionResult.TotalEntries);

		return Result.Success(submissionResult);
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
		var defaultPlugin = _pluginManager.WorklogUploadPlugins.FirstOrDefault();
		if (defaultPlugin == null)
		{
			return Result.Failure<SubmissionResult>("No worklog upload plugin available");
		}

		return await SubmitWeeklyWorklogAsync(date, defaultPlugin.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		_logger.LogInformation("Submitting weekly worklog for week of {Date} using plugin {Plugin}", date.ToShortDateString(), plugin.Metadata.Name);

		var (weekStart, weekEnd) = _dateRangeService.GetWeekRange(date);
		var entries = await _workEntryService.GetWorkEntriesByDateRangeAsync(weekStart, weekEnd, cancellationToken);
		var allWorklogs = entries
			.Where(e => e.EndTime.HasValue)
			.Select(ToWorklogDto)
			.ToList();

		if (allWorklogs.Count == 0)
		{
			_logger.LogWarning("No completed entries found for week of {Date}", date);
			return Result.Success(new SubmissionResult
			{
				TotalEntries = 0,
				SuccessfulEntries = 0,
				FailedEntries = 0
			});
		}

		// Validate and submit
		var validWorklogs = new List<WorklogDto>();
		foreach (var worklog in allWorklogs)
		{
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

		var result = await plugin.UploadWorklogsAsync(validWorklogs.ToPluginWorklogs(), cancellationToken);

		if (result.IsFailure)
		{
			return Result.Failure<SubmissionResult>(result.Error!);
		}

		var pluginResult = result.Value!;
		var submissionResult = new SubmissionResult
		{
			TotalEntries = pluginResult.TotalEntries,
			SuccessfulEntries = pluginResult.SuccessfulEntries,
			FailedEntries = pluginResult.FailedEntries
		};

		return Result.Success(submissionResult);
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
		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found");
		}

		var worklogList = worklogs.ToList();

		_logger.LogInformation("Submitting {Count} custom worklogs using plugin {Plugin}", worklogList.Count, plugin.Metadata.Name);

		var result = await plugin.UploadWorklogsAsync(worklogList.ToPluginWorklogs(), cancellationToken);

		if (result.IsFailure)
		{
			return Result.Failure<SubmissionResult>(result.Error!);
		}

		var pluginResult = result.Value!;
		var submissionResult = new SubmissionResult
		{
			TotalEntries = pluginResult.TotalEntries,
			SuccessfulEntries = pluginResult.SuccessfulEntries,
			FailedEntries = pluginResult.FailedEntries
		};

		return Result.Success(submissionResult);
	}
}
