using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Runs <see cref="PomodoroCycle"/> on a timer and acts on what happens in it: status indicators,
/// notifications and automatic tracking.
/// </summary>
public sealed class PomodoroService : IPomodoroService, IAsyncDisposable
{
	private readonly ISettingsService _settingsService;
	private readonly INotificationService _notificationService;
	private readonly ISystemNotificationService _systemNotification;
	private readonly IWorklogStateService _worklogStateService;
	private readonly IPluginManager _pluginManager;
	private readonly ILocalizationService _localization;
	private readonly IActionFeedbackService _actionFeedback;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PomodoroService> _logger;

	private readonly Lock _lock = new();
	private readonly PomodoroCycle _cycle = new();
	private ITimer? _timer;
	private bool _disposed;

	// Remember what was being tracked for auto-resume after break
	private string? _lastTicketId;
	private string? _lastDescription;

	public PomodoroService(
		ISettingsService settingsService,
		INotificationService notificationService,
		ISystemNotificationService systemNotification,
		IWorklogStateService worklogStateService,
		IPluginManager pluginManager,
		ILocalizationService localization,
		IActionFeedbackService actionFeedback,
		TimeProvider timeProvider,
		ILogger<PomodoroService> logger)
	{
		_actionFeedback = actionFeedback;
		_settingsService = settingsService;
		_notificationService = notificationService;
		_systemNotification = systemNotification;
		_worklogStateService = worklogStateService;
		_pluginManager = pluginManager;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;

		_pluginManager.PluginStarted += OnPluginStarted;
	}

	public PomodoroPhase CurrentPhase => _cycle.Phase;

	public TimeSpan TimeRemaining => _cycle.TimeRemaining;

	public int CompletedPomodoros => _cycle.CompletedPomodoros;

	public int PomodorosBeforeLongBreak => _cycle.Settings.PomodorosBeforeLongBreak;

	public bool IsRunning => _cycle.IsRunning;

	public PomodoroSnapshot GetSnapshot()
	{
		lock (_lock)
		{
			return new PomodoroSnapshot(
				_cycle.Phase,
				_cycle.TimeRemaining,
				_cycle.CompletedPomodoros,
				_cycle.Settings.PomodorosBeforeLongBreak,
				_cycle.IsRunning);
		}
	}

	public event EventHandler<PomodoroPhase>? PhaseChanged;
	public event EventHandler? Tick;
	public event EventHandler? PomodoroCompleted;

	public void Start()
	{
		if (_disposed)
		{
			return;
		}

		PomodoroPhase newPhase;

		lock (_lock)
		{
			if (!_cycle.Start(_settingsService.Settings.Pomodoro))
			{
				return;
			}

			newPhase = _cycle.Phase;

			RememberCurrentTracking();
			StartTimer();
		}

		_logger.LogInformation("Pomodoro started: {Phase}, {Duration}min", newPhase, _cycle.Settings.WorkMinutes);

		FireAndForget(SetStatusIndicatorsForPhaseAsync(newPhase));
		AutoStartTracking();
		PhaseChanged?.Invoke(this, newPhase);
	}

	public void Stop()
	{
		if (_disposed)
		{
			return;
		}

		lock (_lock)
		{
			if (!_cycle.Stop())
			{
				return;
			}

			StopTimer();
		}

		_logger.LogInformation("Pomodoro stopped");

		FireAndForget(SetStatusIndicatorsForPhaseAsync(PomodoroPhase.Idle));
		PhaseChanged?.Invoke(this, PomodoroPhase.Idle);
	}

	public void Skip()
	{
		if (_disposed)
		{
			return;
		}

		lock (_lock)
		{
			if (!_cycle.IsRunning)
			{
				return;
			}
		}

		_logger.LogInformation("Pomodoro phase skipped");
		TransitionToNextPhase();
	}

	public void Reset()
	{
		if (_disposed)
		{
			return;
		}

		lock (_lock)
		{
			_cycle.Reset();
			StopTimer();
		}

		_logger.LogInformation("Pomodoro reset");

		FireAndForget(SetStatusIndicatorsForPhaseAsync(PomodoroPhase.Idle));
		PhaseChanged?.Invoke(this, PomodoroPhase.Idle);
	}

	private void OnTimerTick(object? state)
	{
		if (_disposed)
		{
			return;
		}

		bool phaseExpired;

		lock (_lock)
		{
			if (!_cycle.IsRunning)
			{
				return;
			}

			phaseExpired = _cycle.Tick(TimeSpan.FromSeconds(1));
		}

		if (phaseExpired)
		{
			TransitionToNextPhase();
		}
		else
		{
			Tick?.Invoke(this, EventArgs.Empty);
		}
	}

	private void TransitionToNextPhase()
	{
		PomodoroTransition? transition;

		lock (_lock)
		{
			transition = _cycle.Advance();
		}

		if (transition is not { From: var oldPhase, To: var newPhase, CompletedPomodoro: var pomodoroCompleted })
		{
			return;
		}

		_logger.LogInformation("Pomodoro phase transition: {OldPhase} -> {NewPhase}", oldPhase, newPhase);

		// Notifications
		if (pomodoroCompleted)
		{
			_notificationService.ShowSuccess(_localization["PomodoroCompleted"]);
			PomodoroCompleted?.Invoke(this, EventArgs.Empty);
		}

		if (oldPhase != PomodoroPhase.Work && newPhase == PomodoroPhase.Work)
		{
			_notificationService.ShowInformation(_localization["PomodoroBreakOver"]);
		}

		// System (OS) notifications
		ShowSystemNotificationForPhase(newPhase);

		// Status indicators (LED devices etc.)
		FireAndForget(SetStatusIndicatorsForPhaseAsync(newPhase));

		// Auto-tracking
		if (oldPhase == PomodoroPhase.Work && newPhase != PomodoroPhase.Work)
		{
			AutoStopTracking();
		}
		else if (oldPhase != PomodoroPhase.Work && newPhase == PomodoroPhase.Work)
		{
			AutoStartTracking();
		}

		PhaseChanged?.Invoke(this, newPhase);
	}

	/// <summary>
	/// A status indicator that just started knows nothing about the phase: it was enabled or reconfigured
	/// during a Pomodoro, or a connection test put it aside for a moment. With no Pomodoro running there is
	/// nothing to show, and the device stays as the plugin left it.
	/// </summary>
	private void OnPluginStarted(object? sender, IPlugin plugin)
	{
		var phase = _cycle.Phase;
		if (_disposed || phase == PomodoroPhase.Idle || plugin is not IStatusIndicatorPlugin indicator)
		{
			return;
		}

		FireAndForget(SetPluginStateSafe(indicator, ToIndicatorState(phase), phase));
	}

	private static StatusIndicatorState ToIndicatorState(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Work => StatusIndicatorState.Work,
		PomodoroPhase.ShortBreak => StatusIndicatorState.ShortBreak,
		PomodoroPhase.LongBreak => StatusIndicatorState.LongBreak,
		_ => StatusIndicatorState.Idle
	};

	private async Task SetStatusIndicatorsForPhaseAsync(PomodoroPhase phase)
	{
		var state = ToIndicatorState(phase);

		var tasks = _pluginManager.StatusIndicatorPlugins
			.Select(plugin => SetPluginStateSafe(plugin, state, phase));
		await Task.WhenAll(tasks);
	}

	private async Task SetPluginStateSafe(IStatusIndicatorPlugin plugin, StatusIndicatorState state, PomodoroPhase phase)
	{
		try
		{
			await plugin.SetStateAsync(state, CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to set status indicator for phase {Phase} on plugin {Plugin}", phase, plugin.Metadata.Name);
		}
	}

	private void ShowSystemNotificationForPhase(PomodoroPhase phase)
	{
		if (!_settingsService.Settings.Pomodoro.ShowSystemNotifications)
		{
			return;
		}

		var message = phase switch
		{
			PomodoroPhase.Work => _localization["PomodoroWorkPhaseStarted"],
			PomodoroPhase.ShortBreak => _localization["PomodoroBreakStarted"],
			PomodoroPhase.LongBreak => _localization["PomodoroLongBreakStarted"],
			_ => null
		};

		if (message == null)
		{
			return;
		}

		FireAndForget(ShowSystemNotificationSafe(message));
	}

	private void RememberCurrentTracking()
	{
		var active = _worklogStateService.IsInitialized ? _worklogStateService.ActiveWork : null;
		if (active != null)
		{
			_lastTicketId = active.TicketId;
			_lastDescription = active.Description;
		}
	}

	private void AutoStartTracking()
	{
		if (!_cycle.Settings.AutoStartWorkTracking)
		{
			return;
		}

		if (!_worklogStateService.IsInitialized)
		{
			return;
		}

		if (_worklogStateService.IsTracking)
		{
			return;
		}

		if (_lastTicketId == null && _lastDescription == null)
		{
			return;
		}

		FireAndForget(AutoStartTrackingSafe());
	}

	private void AutoStopTracking()
	{
		if (!_cycle.Settings.AutoStopWorkTracking)
		{
			return;
		}

		if (!_worklogStateService.IsInitialized)
		{
			return;
		}

		if (!_worklogStateService.IsTracking)
		{
			return;
		}

		// Remember before stopping
		RememberCurrentTracking();

		FireAndForget(AutoStopTrackingSafe());
	}

	private async Task ShowSystemNotificationSafe(string message)
	{
		try
		{
			await _systemNotification.ShowNotificationAsync(_localization["Pomodoro"], message);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to show system notification");
		}
	}

	private async Task AutoStartTrackingSafe()
	{
		try
		{
			var result = await _worklogStateService.StartTrackingAsync(_lastTicketId, _lastDescription, CancellationToken.None);
			if (result.IsFailure)
			{
				await ReportAutoTrackingFailureAsync("PomodoroAutoStartFailed", _localization.LocalizeError(result));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to auto-start work tracking");
			await ReportAutoTrackingFailureAsync("PomodoroAutoStartFailed", ex.Message);
		}
	}

	private async Task AutoStopTrackingSafe()
	{
		try
		{
			var result = await _worklogStateService.StopTrackingAsync(CancellationToken.None);
			if (result.IsFailure)
			{
				await ReportAutoTrackingFailureAsync("PomodoroAutoStopFailed", _localization.LocalizeError(result));
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to auto-stop work tracking");
			await ReportAutoTrackingFailureAsync("PomodoroAutoStopFailed", ex.Message);
		}
	}

	/// <summary>
	/// Nobody clicked anything here, so without a report the user would only find out from the entries:
	/// work still being tracked through a break, or a work phase that was never tracked.
	/// </summary>
	private Task ReportAutoTrackingFailureAsync(string messageKey, string reason)
	{
		_logger.LogWarning("Pomodoro auto tracking failed: {Reason}", reason);
		return _actionFeedback.ReportFailureAsync(_localization.GetFormattedString(messageKey, reason), CancellationToken.None);
	}

	private void FireAndForget(Task task)
	{
		task.ContinueWith(
			t => _logger.LogError(t.Exception, "Unobserved error in background task"),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	private void StartTimer()
	{
		StopTimer();
		_timer = _timeProvider.CreateTimer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}

	private void StopTimer()
	{
		_timer?.Dispose();
		_timer = null;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		_pluginManager.PluginStarted -= OnPluginStarted;
		StopTimer();

		// Outside a Pomodoro there is nothing to turn off, and asking would open a device nothing has used
		if (_cycle.Phase == PomodoroPhase.Idle)
		{
			GC.SuppressFinalize(this);
			return;
		}

		// Best-effort: turn off indicators with a timeout to avoid blocking app shutdown
		try
		{
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			await SetStatusIndicatorsForPhaseAsync(PomodoroPhase.Idle).WaitAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			_logger.LogWarning("Timed out waiting for status indicators to turn off during dispose");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to turn off status indicators during dispose");
		}

		GC.SuppressFinalize(this);
	}
}
