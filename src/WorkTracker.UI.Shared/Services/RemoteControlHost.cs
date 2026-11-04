using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Gives remote control plugins access to the application. Commands run on the UI thread: the services
/// raise their events on the caller's thread, and view models subscribed to them touch UI objects.
/// </summary>
public sealed class RemoteControlHost : IRemoteControlHost
{
	private readonly IWorklogStateService _worklogStateService;
	private readonly IPomodoroService _pomodoroService;
	private readonly ISettingsService _settingsService;
	private readonly IWorkSuggestionOrchestrator _suggestionOrchestrator;
	private readonly IDialogService _dialogService;
	private readonly IActionFeedbackService _actionFeedback;
	private readonly ILocalizationService _localization;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUiDispatcher _uiDispatcher;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<RemoteControlHost> _logger;

	public RemoteControlHost(
		IWorklogStateService worklogStateService,
		IPomodoroService pomodoroService,
		ISettingsService settingsService,
		IWorkSuggestionOrchestrator suggestionOrchestrator,
		IDialogService dialogService,
		IActionFeedbackService actionFeedback,
		ILocalizationService localization,
		IServiceScopeFactory scopeFactory,
		IUiDispatcher uiDispatcher,
		TimeProvider timeProvider,
		ILogger<RemoteControlHost> logger)
	{
		_worklogStateService = worklogStateService;
		_pomodoroService = pomodoroService;
		_settingsService = settingsService;
		_suggestionOrchestrator = suggestionOrchestrator;
		_dialogService = dialogService;
		_actionFeedback = actionFeedback;
		_localization = localization;
		_scopeFactory = scopeFactory;
		_uiDispatcher = uiDispatcher;
		_timeProvider = timeProvider;
		_logger = logger;

		_worklogStateService.ActiveWorkChanged += (_, _) => RaiseStateChanged();
		_worklogStateService.WorkEntriesModified += (_, _) => RaiseStateChanged();
		_pomodoroService.PhaseChanged += (_, _) => RaiseStateChanged();
	}

	public event EventHandler? StateChanged;

	public async Task<RemoteControlState> GetStateAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken)
	{
		var activeWork = _worklogStateService.ActiveWork;
		var lastWork = activeWork == null ? await FindLastWorkAsync(lastWorkMaxAge, cancellationToken) : null;
		var pomodoro = _pomodoroService.GetSnapshot();

		return new RemoteControlState(
			ToRemoteWorkItem(activeWork),
			ToRemoteWorkItem(lastWork),
			new RemotePomodoroState(
				ToIndicatorState(pomodoro.CurrentPhase),
				pomodoro.TimeRemaining,
				pomodoro.CompletedPomodoros,
				pomodoro.PomodorosBeforeLongBreak));
	}

	public Task<PluginResult> StartWorkAsync(string? ticketId, string? description, CancellationToken cancellationToken)
	{
		return RunAsync(
			async () => ToPluginResult(await _worklogStateService.StartTrackingAsync(ticketId, description, cancellationToken)),
			() => _localization.GetFormattedString("RemoteWorkStarted", DescribeWork(ticketId, description)),
			cancellationToken);
	}

	public Task<PluginResult> StopWorkAsync(CancellationToken cancellationToken)
	{
		var stoppedWork = string.Empty;

		return RunAsync(
			async () =>
			{
				var activeWork = _worklogStateService.ActiveWork;
				stoppedWork = DescribeWork(activeWork?.TicketId, activeWork?.Description);
				return ToPluginResult(await _worklogStateService.StopTrackingAsync(cancellationToken));
			},
			() => _localization.GetFormattedString("RemoteWorkStopped", stoppedWork),
			cancellationToken);
	}

	public async Task<PluginResult> ResumeLastWorkAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken)
	{
		var lastWork = await FindLastWorkAsync(lastWorkMaxAge, cancellationToken);
		if (lastWork == null)
		{
			return await FailAsync(_localization["RemoteNoRecentWork"], PluginErrorCategory.NotFound, cancellationToken);
		}

		return await StartWorkAsync(lastWork.TicketId, lastWork.Description, cancellationToken);
	}

	/// <remarks>
	/// Reports only a failure: the dial is a continuous gesture with feedback on the device itself.
	/// </remarks>
	public Task<PluginResult> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken)
	{
		return RunAsync(
			async () =>
			{
				var activeWork = _worklogStateService.ActiveWork;
				if (activeWork == null)
				{
					return PluginResult.Failure(_localization["RemoteNoActiveWork"], PluginErrorCategory.NotFound);
				}

				var newStart = activeWork.StartTime + delta;
				if (newStart > _timeProvider.GetLocalNow().DateTime)
				{
					return PluginResult.Failure(_localization["RemoteStartInFuture"], PluginErrorCategory.Validation);
				}

				return ToPluginResult(await _worklogStateService.UpdateWorkEntryAsync(
					activeWork.Id, activeWork.TicketId, newStart, null, activeWork.Description, cancellationToken));
			},
			() => null,
			cancellationToken);
	}

	public Task<PluginResult> AddWorkEntryAsync(string? ticketId, string? description, DateTimeOffset startTime, DateTimeOffset? endTime, CancellationToken cancellationToken)
	{
		return RunAsync(
			async () =>
			{
				var start = ToLocalTime(startTime);
				DateTime? end = endTime == null ? null : ToLocalTime(endTime.Value);

				var plan = await _worklogStateService.ComputeOverlapResolutionAsync(null, start, end, cancellationToken);
				if (plan.HasAdjustments && !plan.IsOnlyClosingActiveEntry)
				{
					return PluginResult.Failure(_localization["RemoteEntryWouldChangeOthers"], PluginErrorCategory.Validation);
				}

				return plan.HasAdjustments
					? ToPluginResult(await _worklogStateService.CreateWorkEntryWithResolutionAsync(ticketId, start, description, end, plan, cancellationToken))
					: ToPluginResult(await _worklogStateService.CreateWorkEntryAsync(ticketId, start, description, end, cancellationToken));
			},
			() => _localization.GetFormattedString("RemoteEntryAdded", DescribeWork(ticketId, description)),
			cancellationToken);
	}

	public async Task<RemoteSuggestions> GetSuggestionsAsync(CancellationToken cancellationToken)
	{
		var today = _timeProvider.GetLocalNow().Date;
		var groups = await _suggestionOrchestrator.GetGroupedSuggestionsAsync(today, cancellationToken);

		return new RemoteSuggestions(
			groups.SelectMany(g => g.Items)
				.Select(s => new RemoteSuggestion(s.Title, s.TicketId, ToLocalOffset(s.StartTime), ToLocalOffset(s.EndTime), s.Source))
				.ToList(),
			groups.Where(g => g.Error != null).Select(g => g.PluginName).ToList());
	}

	public IReadOnlyList<RemoteFavorite> GetFavorites()
	{
		return _settingsService.Settings.FavoriteWorkItems
			.Select(f => new RemoteFavorite(f.Name, f.TicketId, f.Description, f.ShowAsTemplate))
			.ToList();
	}

	public Task<PluginResult> ShowNewWorkEntryDialogAsync(string? ticketId, string? description, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(() =>
		{
			// Not awaited: it completes when the user closes the dialog, and the caller cannot wait for that
			_ = ShowNewWorkEntryDialogCoreAsync(ticketId, description);
		}, cancellationToken);
	}

	public Task<PluginResult> StartPomodoroAsync(CancellationToken cancellationToken)
	{
		if (!_settingsService.Settings.Pomodoro.Enabled)
		{
			return FailAsync(_localization["RemotePomodoroDisabled"], PluginErrorCategory.Validation, cancellationToken);
		}

		return RunPomodoroCommandAsync(_pomodoroService.Start, "RemotePomodoroStarted", cancellationToken);
	}

	public Task<PluginResult> StopPomodoroAsync(CancellationToken cancellationToken)
	{
		return RunPomodoroCommandAsync(_pomodoroService.Stop, "RemotePomodoroStopped", cancellationToken);
	}

	/// <remarks>
	/// Reports nothing of its own: the phase transition already shows its notification.
	/// </remarks>
	public Task<PluginResult> SkipPomodoroPhaseAsync(CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(_pomodoroService.Skip, cancellationToken);
	}

	private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

	/// <summary>
	/// Runs a command on the UI thread and tells the user how it turned out. The report lives here and
	/// not on the state events because those cannot tell a remote command from a click or the Pomodoro
	/// timer. A failure is always reported; a success only if the settings ask for it and
	/// <paramref name="successMessage"/> has something to say.
	/// </summary>
	private async Task<PluginResult> RunAsync(Func<Task<PluginResult>> action, Func<string?> successMessage, CancellationToken cancellationToken)
	{
		PluginResult result;
		try
		{
			result = await OnUiThreadAsync(action, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			await _actionFeedback.ReportFailureAsync(_localization.GetFormattedString("RemoteActionFailed", ex.Message), cancellationToken);
			throw;
		}

		if (result.IsFailure)
		{
			await _actionFeedback.ReportFailureAsync(result.Error!, cancellationToken);
		}
		else if (_settingsService.Settings.ShowRemoteControlNotifications && successMessage() is { } message)
		{
			await _actionFeedback.ReportSuccessAsync(message, cancellationToken);
		}

		return result;
	}

	private async Task<PluginResult> FailAsync(string error, PluginErrorCategory category, CancellationToken cancellationToken)
	{
		await _actionFeedback.ReportFailureAsync(error, cancellationToken);
		return PluginResult.Failure(error, category);
	}

	/// <summary>
	/// Start and Stop quietly do nothing when the timer is already in that state, and then there is
	/// nothing to confirm.
	/// </summary>
	private Task<PluginResult> RunPomodoroCommandAsync(Action command, string successMessageKey, CancellationToken cancellationToken)
	{
		var changed = false;

		return RunAsync(
			() =>
			{
				var wasRunning = _pomodoroService.IsRunning;
				command();
				changed = _pomodoroService.IsRunning != wasRunning;
				return Task.FromResult(PluginResult.Success());
			},
			() => changed ? _localization[successMessageKey] : null,
			cancellationToken);
	}

	private static string DescribeWork(string? ticketId, string? description)
	{
		return string.Join(" – ", new[] { ticketId, description }.Where(part => !string.IsNullOrWhiteSpace(part)));
	}

	private Task<PluginResult> OnUiThreadAsync(Func<Task<PluginResult>> action, CancellationToken cancellationToken)
	{
		return _uiDispatcher.InvokeAsync(action, cancellationToken);
	}

	private Task<PluginResult> OnUiThreadAsync(Action action, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(() =>
		{
			action();
			return Task.FromResult(PluginResult.Success());
		}, cancellationToken);
	}

	private async Task ShowNewWorkEntryDialogCoreAsync(string? ticketId, string? description)
	{
		try
		{
			await _dialogService.ShowNewWorkEntryDialogAsync(ticketId, description);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "The new work entry dialog failed");
		}
	}

	private async Task<WorkEntry?> FindLastWorkAsync(TimeSpan maxAge, CancellationToken cancellationToken)
	{
		var now = _timeProvider.GetLocalNow().DateTime;
		var oldest = now - maxAge;

		using var scope = _scopeFactory.CreateScope();
		var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();
		var entries = await workEntryService.GetWorkEntriesByDateRangeAsync(oldest, now, cancellationToken);

		// The range covers whole days, so it can return entries from earlier on the oldest day
		return entries.Where(e => e.StartTime >= oldest).MaxBy(e => e.StartTime);
	}

	private RemoteWorkItem? ToRemoteWorkItem(WorkEntry? entry)
	{
		if (entry == null)
		{
			return null;
		}

		return new RemoteWorkItem(entry.TicketId, entry.Description, ToLocalOffset(entry.StartTime));
	}

	private DateTime ToLocalTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _timeProvider.LocalTimeZone).DateTime;

	private DateTimeOffset? ToLocalOffset(DateTime? time) => time == null ? null : ToLocalOffset(time.Value);

	private DateTimeOffset ToLocalOffset(DateTime time)
	{
		var local = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
		return new DateTimeOffset(local, _timeProvider.LocalTimeZone.GetUtcOffset(local));
	}

	private static StatusIndicatorState ToIndicatorState(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Work => StatusIndicatorState.Work,
		PomodoroPhase.ShortBreak => StatusIndicatorState.ShortBreak,
		PomodoroPhase.LongBreak => StatusIndicatorState.LongBreak,
		_ => StatusIndicatorState.Idle
	};

	private static PluginResult ToPluginResult(Result result)
	{
		return result.IsSuccess ? PluginResult.Success() : PluginResult.Failure(result.Error, PluginErrorCategory.Validation);
	}
}
