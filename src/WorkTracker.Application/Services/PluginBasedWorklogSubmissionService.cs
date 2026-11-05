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

	public async Task<IReadOnlyList<WorklogSubmissionDto>> PreviewAsync(SubmissionPeriod period, WorklogSubmissionMode mode, CancellationToken cancellationToken)
	{
		var days = await LoadDaysAsync(period, cancellationToken);

		if (mode == WorklogSubmissionMode.Aggregated)
		{
			foreach (var day in days)
			{
				day.Worklogs = Aggregate(day.Worklogs);
			}
		}

		return days;
	}

	public async Task<Result<SubmissionResult>> SubmitAsync(SubmissionPeriod period, string? providerId, CancellationToken cancellationToken)
	{
		var plugin = ResolvePlugin(providerId);
		if (plugin.IsFailure)
		{
			return Result.Failure<SubmissionResult>(plugin);
		}

		_logger.LogInformation("Submitting worklogs for {Period} using plugin {Plugin}", period, plugin.Value.Metadata.Name);

		var days = await LoadDaysAsync(period, cancellationToken);
		var worklogs = days.SelectMany(day => day.Worklogs).ToList();

		if (worklogs.Count == 0)
		{
			_logger.LogWarning("No completed entries found for {Period}", period);
			return Result.Success(new SubmissionResult());
		}

		var result = await ValidateAndUploadAsync(plugin.Value, worklogs, WorklogSubmissionMode.Timed, cancellationToken);

		if (result.IsSuccess)
		{
			_logger.LogInformation("Submitted {Successful}/{Total} worklogs successfully", result.Value!.SuccessfulEntries, result.Value!.TotalEntries);
		}

		return result;
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

		var plugin = ResolvePlugin(providerId);
		if (plugin.IsFailure)
		{
			return Result.Failure<SubmissionResult>(plugin);
		}

		if (!plugin.Value.SupportedModes.HasFlag(mode))
		{
			return Result.Failure<SubmissionResult>($"Plugin '{plugin.Value.Metadata.Name}' does not support submission mode '{mode}'");
		}

		var worklogList = worklogs.ToList();

		_logger.LogInformation("Submitting {Count} custom worklogs using plugin {Plugin} (mode={Mode})",
			worklogList.Count, plugin.Value.Metadata.Name, mode);

		return await ValidateAndUploadAsync(plugin.Value, worklogList, mode, cancellationToken);
	}

	/// <summary>
	/// The completed entries of the period as worklogs, one item per day. A preview and a submission of
	/// the same period both start here, so what gets sent is what was shown.
	/// </summary>
	private async Task<List<WorklogSubmissionDto>> LoadDaysAsync(SubmissionPeriod period, CancellationToken cancellationToken)
	{
		var (start, end) = period.IsWeek ? _dateRangeService.GetWeekRange(period.Date) : (period.Date, period.Date);
		var entries = await _workEntryService.GetWorkEntriesByDateRangeAsync(start, end, cancellationToken);
		var completedByDate = entries
			.Where(e => e.EndTime.HasValue)
			.GroupBy(e => e.StartTime.Date)
			.ToDictionary(g => g.Key, g => g.Select(ToWorklogDto).ToList());

		var days = new List<WorklogSubmissionDto>();

		for (var day = start; day <= end; day = day.AddDays(1))
		{
			days.Add(new WorklogSubmissionDto
			{
				SubmissionDate = day,
				Worklogs = completedByDate.GetValueOrDefault(day) ?? []
			});
		}

		return days;
	}

	/// <summary>
	/// One worklog per ticket and description. The earliest start of a group gives the plugin a valid
	/// time to submit; it stands for the group, so the row has no real interval.
	/// </summary>
	private static List<WorklogDto> Aggregate(IEnumerable<WorklogDto> worklogs)
	{
		return worklogs
			.GroupBy(w => (Ticket: w.TicketId ?? string.Empty, Description: w.Description ?? string.Empty))
			.Select(group =>
			{
				var earliestStart = group.Min(w => w.StartTime);
				return new WorklogDto
				{
					TicketId = group.Key.Ticket.Length == 0 ? null : group.Key.Ticket,
					Description = group.Key.Description,
					StartTime = earliestStart,
					EndTime = earliestStart,
					DurationMinutes = group.Sum(w => w.DurationMinutes)
				};
			})
			.OrderBy(w => w.StartTime)
			.ToList();
	}

	private Result<IWorklogUploadPlugin> ResolvePlugin(string? providerId)
	{
		if (providerId == null)
		{
			var only = ResolveOnlyPlugin();
			if (only.IsFailure)
			{
				return Result.Failure<IWorklogUploadPlugin>(only);
			}

			providerId = only.Value.Metadata.Id;
		}

		var plugin = _pluginManager.GetPlugin<IWorklogUploadPlugin>(providerId);
		return plugin != null
			? Result.Success(plugin)
			: Result.Failure<IWorklogUploadPlugin>($"Plugin '{providerId}' not found or not running");
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
		var valid = new List<int>();
		var rejected = new List<SubmissionError>();

		for (var index = 0; index < worklogs.Count; index++)
		{
			var worklog = worklogs[index];
			var validation = _validator.Validate(worklog);
			if (validation.IsValid)
			{
				valid.Add(index);
			}
			else
			{
				var reason = string.Join(", ", validation.Errors);
				_logger.LogWarning("Not submitting invalid worklog: {Errors}", reason);
				rejected.Add(new SubmissionError
				{
					Date = worklog.StartTime.Date,
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

		var result = await UploadAndMapResultAsync(plugin, worklogs, valid, mode, cancellationToken);
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
		List<int> valid,
		WorklogSubmissionMode mode,
		CancellationToken cancellationToken)
	{
		var result = await plugin.UploadWorklogsAsync(valid.Select(index => worklogs[index].ToPluginWorklog()).ToList(), mode, cancellationToken);

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
			Errors = pluginResult.Errors.Select(e => MapPluginError(e, plugin, worklogs, valid)).ToList()
		});
	}

	/// <summary>
	/// A plugin counts positions in what it was handed, which leaves out the rejected worklogs. The caller
	/// knows the list it submitted, so the position is translated back to that one.
	/// </summary>
	private SubmissionError MapPluginError(WorklogSubmissionError error, IWorklogUploadPlugin plugin, List<WorklogDto> worklogs, List<int> valid)
	{
		if (error.WorklogIndex < 0 || error.WorklogIndex >= valid.Count)
		{
			_logger.LogWarning("Plugin {Plugin} reported a failure at position {Position} of {Count} worklogs", plugin.Metadata.Name, error.WorklogIndex, valid.Count);
			return new SubmissionError { ErrorMessage = error.ErrorMessage };
		}

		var index = valid[error.WorklogIndex];
		return new SubmissionError
		{
			Date = worklogs[index].StartTime.Date,
			ErrorMessage = error.ErrorMessage,
			WorklogIndex = index
		};
	}
}
