using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.DTOs;
using WorkTracker.Application.Plugins;
using WorkTracker.Domain.DTOs;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Application.Services;

/// <summary>
/// Service for submitting worklogs using plugin system
/// </summary>
public sealed class PluginBasedWorklogSubmissionService : IWorklogSubmissionService
{
	private readonly IWorkEntryService _workEntryService;
	private readonly IDateRangeService _dateRangeService;
	private readonly IWorklogValidator _validator;
	private readonly PluginManager _pluginManager;
	private readonly ILogger<PluginBasedWorklogSubmissionService> _logger;

	public PluginBasedWorklogSubmissionService(
		IWorkEntryService workEntryService,
		IDateRangeService dateRangeService,
		IWorklogValidator validator,
		PluginManager pluginManager,
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

		var worklogs = completedEntries.Select(e => new WorklogDto
		{
			TicketId = e.TicketId,
			StartTime = e.StartTime,
			EndTime = e.EndTime!.Value,
			Description = e.Description,
			DurationMinutes = (int)(e.EndTime!.Value - e.StartTime).TotalMinutes
		}).ToList();

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
			Worklogs = completedEntries.Select(e => new WorklogDto
			{
				TicketId = e.TicketId,
				StartTime = e.StartTime,
				EndTime = e.EndTime!.Value,
				Description = e.Description,
				DurationMinutes = (int)(e.EndTime!.Value - e.StartTime).TotalMinutes
			}).ToList()
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
		var allWorklogs = new List<WorklogDto>();

		for (var day = weekStart; day <= weekEnd; day = day.AddDays(1))
		{
			var entries = await _workEntryService.GetWorkEntriesByDateAsync(day, cancellationToken);
			var completedEntries = entries.Where(e => e.EndTime.HasValue);

			foreach (var entry in completedEntries)
			{
				allWorklogs.Add(new WorklogDto
				{
					TicketId = entry.TicketId,
					StartTime = entry.StartTime,
					EndTime = entry.EndTime!.Value,
					Description = entry.Description,
					DurationMinutes = (int)(entry.EndTime!.Value - entry.StartTime).TotalMinutes
				});
			}
		}

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
		var preview = new Dictionary<DateTime, WorklogSubmissionDto>();

		for (var day = weekStart; day <= weekEnd; day = day.AddDays(1))
		{
			preview[day] = await PreviewDailyWorklogAsync(day, cancellationToken);
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
