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
		var plugin = ResolveOnlyPlugin();
		if (plugin.IsFailure)
		{
			return Result.Failure<SubmissionResult>(plugin);
		}

		return await SubmitDailyWorklogAsync(date, plugin.Value.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitDailyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found or not running");
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
		var plugin = ResolveOnlyPlugin();
		if (plugin.IsFailure)
		{
			return Result.Failure<SubmissionResult>(plugin);
		}

		return await SubmitWeeklyWorklogAsync(date, plugin.Value.Metadata.Id, cancellationToken);
	}

	public async Task<Result<SubmissionResult>> SubmitWeeklyWorklogAsync(DateTime date, string providerId, CancellationToken cancellationToken)
	{
		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found or not running");
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
		return _pluginManager.EnabledWorklogUploadPlugins.Select(p => new ProviderInfo
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

		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		if (plugin == null)
		{
			return Result.Failure<SubmissionResult>($"Plugin '{providerId}' not found or not running");
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

	/// <summary>
	/// The plugin to use when the caller named none. With several enabled there is no telling which one
	/// was meant, and worklogs in the wrong system can't be taken back, so this doesn't pick one.
	/// </summary>
	private Result<IWorklogUploadPluginDescriptor> ResolveOnlyPlugin()
	{
		var plugins = _pluginManager.EnabledWorklogUploadPlugins.ToList();

		return plugins.Count switch
		{
			0 => Result.Failure<IWorklogUploadPluginDescriptor>("No worklog upload plugin available"),
			1 => Result.Success(plugins[0]),
			_ => Result.Failure<IWorklogUploadPluginDescriptor>($"Several worklog upload plugins are enabled, choose one: {string.Join(", ", plugins.Select(p => p.Metadata.Id))}")
		};
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
		var valid = new List<(PluginWorklogEntry Worklog, int Index)>();
		var rejected = new List<SubmissionError>();

		for (var index = 0; index < worklogs.Count; index++)
		{
			var worklog = worklogs[index];
			var validation = _validator.Validate(worklog);
			if (validation.IsValid)
			{
				valid.Add((worklog.ToPluginWorklog(), index));
			}
			else
			{
				var reason = string.Join(", ", validation.Errors);
				_logger.LogWarning("Not submitting invalid worklog: {Errors}", reason);
				rejected.Add(new SubmissionError
				{
					TicketId = worklog.TicketId ?? string.Empty,
					Date = worklog.StartTime.Date,
					Description = worklog.Description,
					ErrorMessage = reason,
					WorklogIndex = index
				});
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
		List<(PluginWorklogEntry Worklog, int Index)> worklogs,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		var result = await plugin.UploadWorklogsAsync(worklogs.Select(w => w.Worklog).ToList(), mode, cancellationToken);

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
			Errors = MapPluginErrors(pluginResult.Errors, worklogs)
		});
	}

	/// <summary>
	/// Plugins report a failure with the entry they were handed, which is how it finds its way back to
	/// the worklog. Two entries can look exactly alike, so their values couldn't tell them apart.
	/// </summary>
	private static List<SubmissionError> MapPluginErrors(IReadOnlyList<WorklogSubmissionError> pluginErrors, List<(PluginWorklogEntry Worklog, int Index)> worklogs)
	{
		var positions = worklogs.ToDictionary(w => w.Worklog, w => w.Index);

		return pluginErrors.Select(e => new SubmissionError
		{
			TicketId = e.Worklog.TicketId ?? string.Empty,
			Date = e.Worklog.StartTime.Date,
			Description = e.Worklog.Description,
			ErrorMessage = e.ErrorMessage,
			WorklogIndex = positions.TryGetValue(e.Worklog, out var index) ? index : null
		}).ToList();
	}
}
