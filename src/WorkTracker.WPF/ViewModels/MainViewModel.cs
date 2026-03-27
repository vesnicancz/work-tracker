using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// Main ViewModel for the WorkTracker application
/// Handles work tracking, timer updates, and work entry management
/// </summary>
public class MainViewModel : ViewModelBase, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IDialogService _dialogService;
	private readonly INotificationService _notificationService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly IPomodoroService _pomodoroService;
	private readonly ISettingsService _settingsService;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ILogger<MainViewModel> _logger;
	private readonly DispatcherTimer _timer;
	private readonly CancellationTokenSource _cts = new();
	private bool _disposed;

	private string _elapsedTime = "00:00:00";

	// Pomodoro
	private string _pomodoroTimeRemaining = "00:00";
	private string _pomodoroPhaseDisplay = string.Empty;
	private bool _isPomodoroRunning;
	private string _pomodoroCount = "0/4";

	// Input fields
	private string _workInput = string.Empty;

	private string? _detectedTicketId;
	private string? _detectedDescription;

	// Work entries list
	private ObservableCollection<WorkEntry> _workEntries = new();

	private DateTime _selectedDate;
	private WorkEntry? _selectedWorkEntry;

	// Total time for the selected day
	private string _totalDayDuration = "00:00:00";

	public MainViewModel(
		IServiceScopeFactory scopeFactory,
		IDialogService dialogService,
		INotificationService notificationService,
		IWorklogStateService worklogStateService,
		IPomodoroService pomodoroService,
		ISettingsService settingsService,
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<MainViewModel> logger)
	{
		_scopeFactory = scopeFactory;
		_dialogService = dialogService;
		_notificationService = notificationService;
		_worklogStateService = worklogStateService;
		_pomodoroService = pomodoroService;
		_settingsService = settingsService;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;

		// Subscribe to state change events
		_worklogStateService.ActiveWorkChanged += OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified += OnWorkEntriesModified;

		// Subscribe to Pomodoro events
		_pomodoroService.PhaseChanged += OnPomodoroPhaseChanged;
		_pomodoroService.Tick += OnPomodoroTick;

		// Initialize timer for active work display
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1)
		};
		_timer.Tick += OnTimerTick;

		// Initialize commands
		StartWorkCommand = new AsyncRelayCommand(StartWorkAsync, CanStartWork);
		StopWorkCommand = new AsyncRelayCommand(StopWorkAsync, CanStopWork);
		AddWorkEntryCommand = new AsyncRelayCommand(AddWorkEntryAsync);
		EditWorkEntryCommand = new AsyncRelayCommand<WorkEntry>(EditWorkEntryAsync);
		DeleteWorkEntryCommand = new AsyncRelayCommand<WorkEntry>(DeleteWorkEntryAsync);
		SubmitWorklogCommand = new AsyncRelayCommand(SubmitWorklogAsync);
		RefreshCommand = new AsyncRelayCommand(RefreshWorkEntriesAsync);
		OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
		StartWorkFromHistoryCommand = new AsyncRelayCommand<WorkEntry>(StartWorkFromHistoryAsync);
		PreviousDayCommand = new RelayCommand(PreviousDay);
		NextDayCommand = new RelayCommand(NextDay);
		GoToTodayCommand = new RelayCommand(GoToToday);
		StartPomodoroCommand = new RelayCommand(StartPomodoro);
		StopPomodoroCommand = new RelayCommand(StopPomodoro);
		SkipPomodoroPhaseCommand = new RelayCommand(SkipPomodoroPhase);

		// Initialize data
		_ = InitializeAsync();
	}

	#region Properties

	public WorkEntry? ActiveWork => _worklogStateService.ActiveWork;

	public string ElapsedTime
	{
		get => _elapsedTime;
		set => SetProperty(ref _elapsedTime, value);
	}

	public bool IsTracking => _worklogStateService.IsTracking;

	public string WorkInput
	{
		get => _workInput;
		set
		{
			if (SetProperty(ref _workInput, value))
			{
				ParseWorkInput(value);
				((IAsyncRelayCommand)StartWorkCommand).NotifyCanExecuteChanged();
			}
		}
	}

	public string? DetectedTicketId
	{
		get => _detectedTicketId;
		set => SetProperty(ref _detectedTicketId, value);
	}

	public string? DetectedDescription
	{
		get => _detectedDescription;
		set => SetProperty(ref _detectedDescription, value);
	}

	public ObservableCollection<WorkEntry> WorkEntries
	{
		get => _workEntries;
		set => SetProperty(ref _workEntries, value);
	}

	public DateTime SelectedDate
	{
		get => _selectedDate;
		set
		{
			if (SetProperty(ref _selectedDate, value))
			{
				_ = RefreshWorkEntriesAsync();
			}
		}
	}

	public WorkEntry? SelectedWorkEntry
	{
		get => _selectedWorkEntry;
		set => SetProperty(ref _selectedWorkEntry, value);
	}

	public string ActiveTicketDisplay => ActiveWork?.TicketId ?? _localization["NoTicket"];
	public string ActiveDescriptionDisplay => ActiveWork?.Description ?? _localization["NoDescription"];

	public string TotalDayDuration
	{
		get => _totalDayDuration;
		set => SetProperty(ref _totalDayDuration, value);
	}

	// Pomodoro properties
	public bool IsPomodoroEnabled => _settingsService.Settings.Pomodoro.Enabled;

	public string PomodoroTimeRemaining
	{
		get => _pomodoroTimeRemaining;
		set => SetProperty(ref _pomodoroTimeRemaining, value);
	}

	public string PomodoroPhaseDisplay
	{
		get => _pomodoroPhaseDisplay;
		set => SetProperty(ref _pomodoroPhaseDisplay, value);
	}

	public bool IsPomodoroRunning
	{
		get => _isPomodoroRunning;
		set => SetProperty(ref _isPomodoroRunning, value);
	}

	public string PomodoroCount
	{
		get => _pomodoroCount;
		set => SetProperty(ref _pomodoroCount, value);
	}

	#endregion Properties

	#region Commands

	public ICommand StartWorkCommand { get; }
	public ICommand StopWorkCommand { get; }
	public ICommand AddWorkEntryCommand { get; }
	public ICommand EditWorkEntryCommand { get; }
	public ICommand DeleteWorkEntryCommand { get; }
	public ICommand SubmitWorklogCommand { get; }
	public ICommand RefreshCommand { get; }
	public ICommand OpenSettingsCommand { get; }
	public ICommand StartWorkFromHistoryCommand { get; }
	public ICommand PreviousDayCommand { get; }
	public ICommand NextDayCommand { get; }
	public ICommand GoToTodayCommand { get; }
	public ICommand StartPomodoroCommand { get; }
	public ICommand StopPomodoroCommand { get; }
	public ICommand SkipPomodoroPhaseCommand { get; }

	#endregion Commands

	#region Initialization

	private async Task InitializeAsync()
	{
		try
		{
			// State is already initialized by App.xaml.cs
			// Just initialize timer state based on current tracking state
			if (IsTracking)
			{
				_timer.Start();
			}

			// Load today's entries
			await RefreshWorkEntriesAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to initialize MainViewModel");
			_notificationService.ShowError(_localization["FailedToLoadWorkEntries"]);
		}
	}

	#endregion Initialization

	#region Work Input Parsing

	private void ParseWorkInput(string input)
	{
		var (ticketId, description) = WorkInputParser.Parse(input);
		DetectedTicketId = ticketId;
		DetectedDescription = description;
	}

	#endregion Work Input Parsing

	#region Command Implementations

	private bool CanStartWork()
	{
		return !string.IsNullOrWhiteSpace(WorkInput);
	}

	private async Task StartWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StartTrackingAsync(
				DetectedTicketId,
				DetectedDescription,
				_cts.Token);

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to start work: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			// Clear input
			WorkInput = string.Empty;

			_notificationService.ShowSuccess(_localization["WorkTrackingStarted"]);
		}
		catch (OperationCanceledException) when (_disposed) { }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error starting work");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToStartWork", ex.Message));
		}
	}

	private bool CanStopWork()
	{
		return IsTracking;
	}

	private async Task StopWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StopTrackingAsync(_cts.Token);

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to stop work: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			_notificationService.ShowSuccess(_localization["WorkTrackingStopped"]);
		}
		catch (OperationCanceledException) when (_disposed) { }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error stopping work");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToStopWork", ex.Message));
		}
	}

	private async Task AddWorkEntryAsync()
	{
		try
		{
			var result = await _dialogService.ShowNewWorkEntryDialogAsync(date: SelectedDate);
			if (result)
			{
				_notificationService.ShowSuccess(_localization["WorkEntryCreated"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to create work entry");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToCreateWorkEntry", ex.Message));
		}
	}

	private async Task EditWorkEntryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null)
		{
			return;
		}

		try
		{
			var result = await _dialogService.ShowEditWorkEntryDialogAsync(workEntry);
			if (result)
			{
				_notificationService.ShowSuccess(_localization["WorkEntryUpdated"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to edit work entry");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToEditWorkEntry", ex.Message));
		}
	}

	private async Task DeleteWorkEntryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null)
		{
			return;
		}

		try
		{
			var confirmed = await _dialogService.ShowConfirmationAsync(
				_localization.GetFormattedString("ConfirmDeleteMessage",
					workEntry.TicketId ?? "N/A",
					workEntry.Description ?? "N/A",
					$"{workEntry.StartTime:HH:mm} - {workEntry.EndTime?.ToString("HH:mm") ?? "Active"}"),
				_localization["ConfirmDelete"]);

			if (confirmed)
			{
				var result = await _worklogStateService.DeleteWorkEntryAsync(workEntry.Id, _cts.Token);

				if (result.IsFailure)
				{
					_logger.LogWarning("Failed to delete work entry: {Error}", result.Error);
					await _dialogService.ShowErrorAsync(result.Error);
					return;
				}

				_notificationService.ShowSuccess(_localization["WorkEntryDeleted"]);
			}
		}
		catch (OperationCanceledException) when (_disposed) { }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to delete work entry");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToDeleteWorkEntry", ex.Message));
		}
	}

	private async Task StartWorkFromHistoryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null)
		{
			return;
		}

		try
		{
			// Start new work with the same ticket and description
			var result = await _worklogStateService.StartTrackingAsync(
				workEntry.TicketId,
				workEntry.Description,
				_cts.Token);

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to restart work from history: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			_notificationService.ShowSuccess(_localization["WorkRestartedSuccessfully"]);
		}
		catch (OperationCanceledException) when (_disposed) { }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error restarting work from history");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToRestartWork", ex.Message));
		}
	}

	private async Task SubmitWorklogAsync()
	{
		try
		{
			var result = await _dialogService.ShowSubmitWorklogDialogAsync(SelectedDate, false);
			if (result)
			{
				_notificationService.ShowSuccess(_localization["WorklogsSubmittedSuccessfully"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to submit worklogs");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToSubmitWorklogs", ex.Message));
		}
	}

	private async Task OpenSettingsAsync()
	{
		try
		{
			await _dialogService.ShowSettingsDialogAsync();
			OnPropertyChanged(nameof(IsPomodoroEnabled));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to open settings");
			await _dialogService.ShowErrorAsync(_localization.GetFormattedString("FailedToOpenSettings", ex.Message));
		}
	}

	private async Task RefreshWorkEntriesAsync()
	{
		try
		{
			_cts.Token.ThrowIfCancellationRequested();
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var entries = await workEntryService.GetWorkEntriesByDateAsync(SelectedDate, _cts.Token);
			WorkEntries = new ObservableCollection<WorkEntry>(entries.OrderBy(e => e.StartTime));
			UpdateTotalDayDuration();
		}
		catch (OperationCanceledException) { /* ViewModel is being disposed */ }
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh work entries");
			_notificationService.ShowError(_localization["FailedToLoadWorkEntries"]);
		}
	}

	private void UpdateTotalDayDuration()
	{
		var totalSeconds = 0.0;

		foreach (var entry in WorkEntries)
		{
			if (entry.IsActive)
			{
				// For active entries, calculate duration from start time to now
				var elapsed = _timeProvider.GetLocalNow().DateTime - entry.StartTime;
				totalSeconds += elapsed.TotalSeconds;
			}
			else if (entry.Duration.HasValue)
			{
				// For completed entries, use the Duration property
				totalSeconds += entry.Duration.Value.TotalSeconds;
			}
		}

		var total = TimeSpan.FromSeconds(totalSeconds);
		TotalDayDuration = $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}";
	}

	private void PreviousDay()
	{
		SelectedDate = SelectedDate.AddDays(-1);
	}

	private void NextDay()
	{
		SelectedDate = SelectedDate.AddDays(1);
	}

	private void GoToToday()
	{
		SelectedDate = _timeProvider.GetLocalNow().Date;
	}

	#endregion Command Implementations

	#region Pomodoro

	private void StartPomodoro() => _pomodoroService.Start();
	private void StopPomodoro() => _pomodoroService.Stop();
	private void SkipPomodoroPhase() => _pomodoroService.Skip();

	private void UpdatePomodoroDisplay()
	{
		var remaining = _pomodoroService.TimeRemaining;
		PomodoroTimeRemaining = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
		PomodoroCount = $"{_pomodoroService.CompletedPomodoros}/{_pomodoroService.PomodorosBeforeLongBreak}";
	}

	private string GetPhaseDisplayText(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Work => _localization["PomodoroWork"],
		PomodoroPhase.ShortBreak => _localization["PomodoroShortBreak"],
		PomodoroPhase.LongBreak => _localization["PomodoroLongBreak"],
		_ => string.Empty
	};

	private void OnPomodoroPhaseChanged(object? sender, PomodoroPhase phase)
	{
		System.Windows.Application.Current?.Dispatcher.Invoke(() =>
		{
			IsPomodoroRunning = _pomodoroService.IsRunning;
			PomodoroPhaseDisplay = GetPhaseDisplayText(phase);
			UpdatePomodoroDisplay();
		});
	}

	private void OnPomodoroTick(object? sender, EventArgs e)
	{
		System.Windows.Application.Current?.Dispatcher.Invoke(UpdatePomodoroDisplay);
	}

	#endregion Pomodoro

	#region Timer

	private void OnTimerTick(object? sender, EventArgs e)
	{
		if (ActiveWork != null)
		{
			var elapsed = _timeProvider.GetLocalNow().DateTime - ActiveWork.StartTime;
			ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

			// Update total day duration to include the running time
			UpdateTotalDayDuration();
		}
	}

	#endregion Timer

	#region Event Handlers

	private async void OnWorkEntriesModified(object? sender, EventArgs e)
	{
		if (_disposed)
		{
			return;
		}
		// Refresh work entries list when notified of changes from external sources (e.g., tray menu, dialogs)
		// Note: WorklogStateService automatically refreshes its own state, we just need to refresh the list
		try
		{
			await RefreshWorkEntriesAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh work entries after modification");
		}
	}

	private void OnActiveWorkChanged(object? sender, WorkEntry? activeWork)
	{
		// Notify UI that ActiveWork property changed
		OnPropertyChanged(nameof(ActiveWork));
		OnPropertyChanged(nameof(ActiveTicketDisplay));
		OnPropertyChanged(nameof(ActiveDescriptionDisplay));

		_logger.LogDebug("ActiveWork changed in ViewModel: WorkEntryId={WorkEntryId}", activeWork?.Id);
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		if (isTracking)
		{
			_timer.Start();
		}
		else
		{
			_timer.Stop();
			ElapsedTime = "00:00:00";
		}

		// Notify UI that IsTracking property changed
		OnPropertyChanged(nameof(IsTracking));

		// Update commands
		((IAsyncRelayCommand)StartWorkCommand).NotifyCanExecuteChanged();
		((IAsyncRelayCommand)StopWorkCommand).NotifyCanExecuteChanged();

		_logger.LogDebug("IsTracking changed in ViewModel: {IsTracking}", isTracking);
	}

	#endregion Event Handlers

	#region IDisposable

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_cts.Dispose();
		_timer.Stop();
		_worklogStateService.ActiveWorkChanged -= OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified -= OnWorkEntriesModified;
		_pomodoroService.PhaseChanged -= OnPomodoroPhaseChanged;
		_pomodoroService.Tick -= OnPomodoroTick;
	}

	#endregion IDisposable
}