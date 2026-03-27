using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Services;

public sealed class PomodoroService : IPomodoroService, IDisposable
{
	private readonly ISettingsService _settingsService;
	private readonly INotificationService _notificationService;
	private readonly ISystemNotificationService _systemNotification;
	private readonly IWorklogStateService _worklogStateService;
	private readonly IPluginManager _pluginManager;
	private readonly ILocalizationService _localization;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PomodoroService> _logger;

	private readonly Lock _lock = new();
	private Timer? _timer;
	private PomodoroPhase _currentPhase = PomodoroPhase.Idle;
	private TimeSpan _timeRemaining;
	private int _completedPomodoros;
	private int _pomodorosBeforeLongBreak = 4;
	private bool _isRunning;
	private bool _disposed;

	// Remember what was being tracked for auto-resume after break
	private string? _lastTicketId;
	private string? _lastDescription;

	// Cached settings for current session
	private PomodoroSettings _activeSettings = new();

	public PomodoroService(
		ISettingsService settingsService,
		INotificationService notificationService,
		ISystemNotificationService systemNotification,
		IWorklogStateService worklogStateService,
		IPluginManager pluginManager,
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<PomodoroService> logger)
	{
		_settingsService = settingsService;
		_notificationService = notificationService;
		_systemNotification = systemNotification;
		_worklogStateService = worklogStateService;
		_pluginManager = pluginManager;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public PomodoroPhase CurrentPhase
	{
		get { lock (_lock)
			{
				return _currentPhase;
			}
		}
	}

	public TimeSpan TimeRemaining
	{
		get { lock (_lock)
			{
				return _timeRemaining;
			}
		}
	}

	public int CompletedPomodoros
	{
		get { lock (_lock)
			{
				return _completedPomodoros;
			}
		}
	}

	public int PomodorosBeforeLongBreak
	{
		get { lock (_lock)
			{
				return _pomodorosBeforeLongBreak;
			}
		}
	}

	public bool IsRunning
	{
		get { lock (_lock)
			{
				return _isRunning;
			}
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
			if (_isRunning)
			{
				return;
			}

			_activeSettings = _settingsService.Settings.Pomodoro;
			_pomodorosBeforeLongBreak = _activeSettings.PomodorosBeforeLongBreak;
			_completedPomodoros = 0;

			_isRunning = true;
			_currentPhase = PomodoroPhase.Work;
			_timeRemaining = TimeSpan.FromMinutes(_activeSettings.WorkMinutes);
			newPhase = _currentPhase;

			RememberCurrentTracking();
			StartTimer();
		}

		_logger.LogInformation("Pomodoro started: {Phase}, {Duration}min", newPhase, _activeSettings.WorkMinutes);

		SetStatusIndicatorsForPhase(newPhase);
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
			if (!_isRunning)
			{
				return;
			}

			_isRunning = false;
			_currentPhase = PomodoroPhase.Idle;
			_timeRemaining = TimeSpan.Zero;
			StopTimer();
		}

		_logger.LogInformation("Pomodoro stopped");

		SetStatusIndicatorsForPhase(PomodoroPhase.Idle);
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
			if (!_isRunning)
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
			_isRunning = false;
			_currentPhase = PomodoroPhase.Idle;
			_timeRemaining = TimeSpan.Zero;
			_completedPomodoros = 0;
			StopTimer();
		}

		_logger.LogInformation("Pomodoro reset");

		SetStatusIndicatorsForPhase(PomodoroPhase.Idle);
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
			if (!_isRunning)
			{
				return;
			}

			_timeRemaining -= TimeSpan.FromSeconds(1);
			phaseExpired = _timeRemaining <= TimeSpan.Zero;
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
		PomodoroPhase oldPhase;
		PomodoroPhase newPhase;
		bool pomodoroCompleted = false;

		lock (_lock)
		{
			oldPhase = _currentPhase;

			switch (_currentPhase)
			{
				case PomodoroPhase.Work:
					_completedPomodoros++;
					pomodoroCompleted = true;

					if (_completedPomodoros >= _activeSettings.PomodorosBeforeLongBreak)
					{
						_currentPhase = PomodoroPhase.LongBreak;
						_timeRemaining = TimeSpan.FromMinutes(_activeSettings.LongBreakMinutes);
					}
					else
					{
						_currentPhase = PomodoroPhase.ShortBreak;
						_timeRemaining = TimeSpan.FromMinutes(_activeSettings.ShortBreakMinutes);
					}
					break;

				case PomodoroPhase.ShortBreak:
					_currentPhase = PomodoroPhase.Work;
					_timeRemaining = TimeSpan.FromMinutes(_activeSettings.WorkMinutes);
					break;

				case PomodoroPhase.LongBreak:
					_completedPomodoros = 0;
					_currentPhase = PomodoroPhase.Work;
					_timeRemaining = TimeSpan.FromMinutes(_activeSettings.WorkMinutes);
					break;

				default:
					return;
			}

			newPhase = _currentPhase;
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

		// Luxafor
		SetStatusIndicatorsForPhase(newPhase);

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

	private void SetStatusIndicatorsForPhase(PomodoroPhase phase)
	{
		var state = phase switch
		{
			PomodoroPhase.Work => StatusIndicatorState.Work,
			PomodoroPhase.ShortBreak => StatusIndicatorState.ShortBreak,
			PomodoroPhase.LongBreak => StatusIndicatorState.LongBreak,
			_ => StatusIndicatorState.Idle
		};

		foreach (var plugin in _pluginManager.StatusIndicatorPlugins)
		{
			try
			{
				_ = plugin.SetStateAsync(state, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to set status indicator for phase {Phase} on plugin {Plugin}", phase, plugin.Metadata.Name);
			}
		}
	}

	private void ShowSystemNotificationForPhase(PomodoroPhase phase)
	{
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

		_ = _systemNotification.ShowNotificationAsync("Pomodoro", message);
	}

	private void RememberCurrentTracking()
	{
		if (_worklogStateService.IsInitialized && _worklogStateService.IsTracking)
		{
			var active = _worklogStateService.ActiveWork;
			_lastTicketId = active?.TicketId;
			_lastDescription = active?.Description;
		}
	}

	private void AutoStartTracking()
	{
		if (!_activeSettings.AutoStartWorkTracking)
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

		_ = Task.Run(async () =>
		{
			try
			{
				await _worklogStateService.StartTrackingAsync(_lastTicketId, _lastDescription, CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to auto-start work tracking");
			}
		});
	}

	private void AutoStopTracking()
	{
		if (!_activeSettings.AutoStopWorkTracking)
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

		_ = Task.Run(async () =>
		{
			try
			{
				await _worklogStateService.StopTrackingAsync(CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to auto-stop work tracking");
			}
		});
	}

	private void StartTimer()
	{
		StopTimer();
		_timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
	}

	private void StopTimer()
	{
		_timer?.Dispose();
		_timer = null;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		StopTimer();
		SetStatusIndicatorsForPhase(PomodoroPhase.Idle);
	}
}
