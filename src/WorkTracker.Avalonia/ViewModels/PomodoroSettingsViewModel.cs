using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// The Pomodoro tab of the Settings window
/// </summary>
public class PomodoroSettingsViewModel : ViewModelBase
{
	private bool _enabled;
	private int _workMinutes;
	private int _shortBreakMinutes;
	private int _longBreakMinutes;
	private int _pomodorosBeforeLongBreak;
	private bool _autoStartTracking;
	private bool _autoStopTracking;
	private bool _showSystemNotifications;

	public PomodoroSettingsViewModel(PomodoroSettings settings)
	{
		_enabled = settings.Enabled;
		_workMinutes = settings.WorkMinutes;
		_shortBreakMinutes = settings.ShortBreakMinutes;
		_longBreakMinutes = settings.LongBreakMinutes;
		_pomodorosBeforeLongBreak = settings.PomodorosBeforeLongBreak;
		_autoStartTracking = settings.AutoStartWorkTracking;
		_autoStopTracking = settings.AutoStopWorkTracking;
		_showSystemNotifications = settings.ShowSystemNotifications;
	}

	public bool Enabled
	{
		get => _enabled;
		set => SetProperty(ref _enabled, value);
	}

	public int WorkMinutes
	{
		get => _workMinutes;
		set => SetProperty(ref _workMinutes, value);
	}

	public int ShortBreakMinutes
	{
		get => _shortBreakMinutes;
		set => SetProperty(ref _shortBreakMinutes, value);
	}

	public int LongBreakMinutes
	{
		get => _longBreakMinutes;
		set => SetProperty(ref _longBreakMinutes, value);
	}

	public int PomodorosBeforeLongBreak
	{
		get => _pomodorosBeforeLongBreak;
		set => SetProperty(ref _pomodorosBeforeLongBreak, value);
	}

	public bool AutoStartTracking
	{
		get => _autoStartTracking;
		set => SetProperty(ref _autoStartTracking, value);
	}

	public bool AutoStopTracking
	{
		get => _autoStopTracking;
		set => SetProperty(ref _autoStopTracking, value);
	}

	public bool ShowSystemNotifications
	{
		get => _showSystemNotifications;
		set => SetProperty(ref _showSystemNotifications, value);
	}

	public PomodoroSettings ToSettings() => new()
	{
		Enabled = Enabled,
		WorkMinutes = WorkMinutes,
		ShortBreakMinutes = ShortBreakMinutes,
		LongBreakMinutes = LongBreakMinutes,
		PomodorosBeforeLongBreak = PomodorosBeforeLongBreak,
		AutoStartWorkTracking = AutoStartTracking,
		AutoStopWorkTracking = AutoStopTracking,
		ShowSystemNotifications = ShowSystemNotifications
	};
}
