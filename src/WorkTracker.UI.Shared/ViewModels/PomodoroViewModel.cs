using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// Shared Pomodoro ViewModel encapsulating timer display state and commands.
/// Platform-specific MainViewModels compose this and wire up dispatcher marshalling.
/// </summary>
public class PomodoroViewModel : ObservableObject, IDisposable
{
	private readonly IPomodoroService _pomodoroService;
	private readonly ISettingsService _settingsService;
	private readonly ILocalizationService _localization;
	private bool _disposed;

	private string _timeRemaining = "00:00";
	private string _phaseDisplay = string.Empty;
	private bool _isRunning;
	private string _count = "0/4";
	private bool _isWork;
	private bool _isShortBreak;
	private bool _isLongBreak;

	public PomodoroViewModel(
		IPomodoroService pomodoroService,
		ISettingsService settingsService,
		ILocalizationService localization)
	{
		_pomodoroService = pomodoroService;
		_settingsService = settingsService;
		_localization = localization;

		StartCommand = new RelayCommand(() => _pomodoroService.Start());
		StopCommand = new RelayCommand(() => _pomodoroService.Stop());
		SkipPhaseCommand = new RelayCommand(() => _pomodoroService.Skip());

		_pomodoroService.PhaseChanged += OnPhaseChanged;
		_pomodoroService.Tick += OnTick;
	}

	#region Properties

	public bool IsEnabled => _settingsService.Settings.Pomodoro.Enabled;

	public string TimeRemaining
	{
		get => _timeRemaining;
		set => SetProperty(ref _timeRemaining, value);
	}

	public string PhaseDisplay
	{
		get => _phaseDisplay;
		set => SetProperty(ref _phaseDisplay, value);
	}

	public bool IsRunning
	{
		get => _isRunning;
		set => SetProperty(ref _isRunning, value);
	}

	public string Count
	{
		get => _count;
		set => SetProperty(ref _count, value);
	}

	public bool IsWork
	{
		get => _isWork;
		set => SetProperty(ref _isWork, value);
	}

	public bool IsShortBreak
	{
		get => _isShortBreak;
		set => SetProperty(ref _isShortBreak, value);
	}

	public bool IsLongBreak
	{
		get => _isLongBreak;
		set => SetProperty(ref _isLongBreak, value);
	}

	#endregion Properties

	#region Commands

	public ICommand StartCommand { get; }
	public ICommand StopCommand { get; }
	public ICommand SkipPhaseCommand { get; }

	#endregion Commands

	/// <summary>
	/// Called by platform-specific code on the UI thread when a phase change occurs.
	/// </summary>
	public void UpdatePhase(PomodoroPhase phase)
	{
		var snapshot = _pomodoroService.GetSnapshot();
		IsRunning = snapshot.IsRunning;
		PhaseDisplay = GetPhaseDisplayText(phase);
		Count = $"{snapshot.CompletedPomodoros}/{snapshot.PomodorosBeforeLongBreak}";
		IsWork = phase == PomodoroPhase.Work;
		IsShortBreak = phase == PomodoroPhase.ShortBreak;
		IsLongBreak = phase == PomodoroPhase.LongBreak;
		TimeRemaining = FormatTimeRemaining(snapshot.TimeRemaining);
	}

	/// <summary>
	/// Called by platform-specific code on the UI thread when a tick occurs.
	/// </summary>
	public void UpdateTimeDisplay()
	{
		TimeRemaining = FormatTimeRemaining(_pomodoroService.TimeRemaining);
	}

	private static string FormatTimeRemaining(TimeSpan remaining) =>
		$"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

	public void RefreshEnabled() => OnPropertyChanged(nameof(IsEnabled));

	/// <summary>
	/// Raised when a phase change comes from the service. Platform code should marshal to UI thread.
	/// </summary>
	public event EventHandler<PomodoroPhase>? PhaseChangedOnService;

	/// <summary>
	/// Raised when a tick comes from the service. Platform code should marshal to UI thread.
	/// </summary>
	public event EventHandler? TickOnService;

	private void OnPhaseChanged(object? sender, PomodoroPhase phase)
	{
		PhaseChangedOnService?.Invoke(this, phase);
	}

	private void OnTick(object? sender, EventArgs e)
	{
		TickOnService?.Invoke(this, EventArgs.Empty);
	}

	private string GetPhaseDisplayText(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Work => _localization["PomodoroWork"],
		PomodoroPhase.ShortBreak => _localization["PomodoroShortBreak"],
		PomodoroPhase.LongBreak => _localization["PomodoroLongBreak"],
		_ => string.Empty
	};

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_pomodoroService.PhaseChanged -= OnPhaseChanged;
		_pomodoroService.Tick -= OnTick;
	}
}