using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IDialogService _dialogService;
	private readonly INotificationService _notificationService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly IWorkSuggestionOrchestrator _suggestionOrchestrator;
	private readonly IPomodoroService _pomodoroService;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ILogger<MainViewModel> _logger;
	private readonly DispatcherTimer _timer;
	private readonly CancellationTokenSource _cts = new();
	private bool _disposed;
	private bool _timerPaused;

	private string _elapsedTime = "00:00:00";

	// Pomodoro theme brushes (Avalonia-specific)
	private IBrush? _pomodoroCardBackground;
	private IBrush? _pomodoroCardBorder;
	private IBrush? _pomodoroTimerForeground;

	private string _workInput = string.Empty;
	private string? _detectedTicketId;
	private string? _detectedDescription;
	private ObservableCollection<WorkEntry> _workEntries = new();
	private DateTime _selectedDate;
	private WorkEntry? _selectedWorkEntry;
	private string _totalDayDuration = "00:00:00";

	public MainViewModel(
		IServiceScopeFactory scopeFactory,
		IDialogService dialogService,
		INotificationService notificationService,
		IWorklogStateService worklogStateService,
		IWorkSuggestionOrchestrator suggestionOrchestrator,
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
		_suggestionOrchestrator = suggestionOrchestrator;
		_pomodoroService = pomodoroService;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;

		// Pomodoro sub-ViewModel with Avalonia dispatcher marshalling
		Pomodoro = new PomodoroViewModel(pomodoroService, settingsService, localization);
		Pomodoro.PhaseChangedOnService += (_, phase) =>
			Dispatcher.UIThread.Post(() => { Pomodoro.UpdatePhase(phase); UpdatePomodoroBrushes(phase); });
		Pomodoro.TickOnService += (_, _) =>
			Dispatcher.UIThread.Post(() => Pomodoro.UpdateTimeDisplay());
		App.ThemeChanged += OnThemeChanged;

		// Initial sync with current service state (may already be running)
		var initialPhase = pomodoroService.CurrentPhase;
		Pomodoro.UpdatePhase(initialPhase);
		UpdatePomodoroBrushes(initialPhase);

		_worklogStateService.ActiveWorkChanged += OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified += OnWorkEntriesModified;

		_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_timer.Tick += OnTimerTick;

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
		OpenSuggestionsCommand = new AsyncRelayCommand(OpenSuggestionsAsync);

		_ = InitializeAsync().ContinueWith(t =>
		{
			if (t.IsFaulted)
			{
				_logger.LogError(t.Exception, "MainViewModel initialization failed");
			}
		}, TaskScheduler.Default);
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
				StartWorkCommand.NotifyCanExecuteChanged();
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

	public PomodoroViewModel Pomodoro { get; }

	// Avalonia-specific theme-aware brushes
	public IBrush? PomodoroCardBackground
	{
		get => _pomodoroCardBackground;
		set => SetProperty(ref _pomodoroCardBackground, value);
	}

	public IBrush? PomodoroCardBorder
	{
		get => _pomodoroCardBorder;
		set => SetProperty(ref _pomodoroCardBorder, value);
	}

	public IBrush? PomodoroTimerForeground
	{
		get => _pomodoroTimerForeground;
		set => SetProperty(ref _pomodoroTimerForeground, value);
	}

	public bool HasSuggestionPlugins => _suggestionOrchestrator.HasSuggestionPlugins;

	#endregion Properties

	#region Commands

	public IAsyncRelayCommand StartWorkCommand { get; }
	public IAsyncRelayCommand StopWorkCommand { get; }
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
	public ICommand OpenSuggestionsCommand { get; }

	#endregion Commands

	private async Task InitializeAsync()
	{
		try
		{
			if (IsTracking)
			{
				_timer.Start();
			}

			await RefreshWorkEntriesAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to initialize MainViewModel");
			_notificationService.ShowError(_localization["FailedToLoadWorkEntries"]);
		}
	}

	private void ParseWorkInput(string input)
	{
		var (ticketId, description) = WorkInputParser.Parse(input);
		DetectedTicketId = ticketId;
		DetectedDescription = description;
	}

	private bool CanStartWork() => !string.IsNullOrWhiteSpace(WorkInput);

	private async Task StartWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StartTrackingAsync(DetectedTicketId, DetectedDescription, _cts.Token);
			if (result.IsFailure)
			{
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}
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

	private bool CanStopWork() => IsTracking;

	private async Task StopWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StopTrackingAsync(_cts.Token);
			if (result.IsFailure)
			{
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
			var result = await _worklogStateService.StartTrackingAsync(workEntry.TicketId, workEntry.Description, _cts.Token);
			if (result.IsFailure)
			{
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
			Pomodoro.RefreshEnabled();
			OnPropertyChanged(nameof(HasSuggestionPlugins));
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
				totalSeconds += (_timeProvider.GetLocalNow().DateTime - entry.StartTime).TotalSeconds;
			}
			else if (entry.Duration.HasValue)
			{
				totalSeconds += entry.Duration.Value.TotalSeconds;
			}
		}
		var total = TimeSpan.FromSeconds(totalSeconds);
		TotalDayDuration = $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}";
	}

	private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

	private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

	private void GoToToday() => SelectedDate = _timeProvider.GetLocalNow().Date;

	#region Suggestions

	private async Task OpenSuggestionsAsync()
	{
		try
		{
			var selected = await _dialogService.ShowSuggestionsDialogAsync(SelectedDate);
			if (selected == null)
			{
				return;
			}

			await _dialogService.ShowNewWorkEntryDialogAsync(
				ticketId: selected.TicketId,
				description: selected.Title,
				date: SelectedDate,
				startTime: selected.StartTime,
				endTime: selected.EndTime);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to open suggestions");
		}
	}

	#endregion Suggestions

	#region Pomodoro Theme Brushes (Avalonia-specific)

	private void OnThemeChanged(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(() => UpdatePomodoroBrushes(_pomodoroService.CurrentPhase));
	}

	private void UpdatePomodoroBrushes(PomodoroPhase phase)
	{
		var (bgKey, borderKey, fgKey) = phase switch
		{
			PomodoroPhase.Work => ("PomodoroWorkCardBackground", "PomodoroWorkCardBorder", "DangerRed"),
			PomodoroPhase.ShortBreak => ("PomodoroBreakCardBackground", "PomodoroBreakCardBorder", "SuccessGreen"),
			PomodoroPhase.LongBreak => ("PomodoroLongBreakCardBackground", "PomodoroLongBreakCardBorder", "InfoBlue"),
			_ => ("CardBackground", "CardBorder", "TextBright")
		};

		var app = global::Avalonia.Application.Current;
		if (app == null)
		{
			return;
		}

		app.TryFindResource(bgKey, app.ActualThemeVariant, out var bg);
		app.TryFindResource(borderKey, app.ActualThemeVariant, out var border);
		app.TryFindResource(fgKey, app.ActualThemeVariant, out var fg);
		PomodoroCardBackground = bg as IBrush;
		PomodoroCardBorder = border as IBrush;
		PomodoroTimerForeground = fg as IBrush;
	}

	#endregion Pomodoro Theme Brushes

	private void OnTimerTick(object? sender, EventArgs e)
	{
		var activeWork = ActiveWork;
		if (activeWork != null)
		{
			var elapsed = _timeProvider.GetLocalNow().DateTime - activeWork.StartTime;
			ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
			UpdateTotalDayDuration();
		}
	}

	private async void OnWorkEntriesModified(object? sender, EventArgs e)
	{
		if (_disposed)
		{
			return;
		}

		try { await RefreshWorkEntriesAsync(); }
		catch (Exception ex) { _logger.LogError(ex, "Failed to refresh work entries after modification"); }
	}

	private void OnActiveWorkChanged(object? sender, WorkEntry? activeWork)
	{
		OnPropertyChanged(nameof(ActiveWork));
		OnPropertyChanged(nameof(ActiveTicketDisplay));
		OnPropertyChanged(nameof(ActiveDescriptionDisplay));
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		if (isTracking && !_timerPaused)
		{
			_timer.Start();
		}
		else
		{
			_timer.Stop();
			ElapsedTime = "00:00:00";
		}

		OnPropertyChanged(nameof(IsTracking));
		StartWorkCommand.NotifyCanExecuteChanged();
		StopWorkCommand.NotifyCanExecuteChanged();
	}

	/// <summary>
	/// Called after plugin initialization completes to refresh plugin-dependent UI.
	/// </summary>
	public void NotifyPluginsLoaded()
	{
		OnPropertyChanged(nameof(HasSuggestionPlugins));
	}

	public void PauseTimer()
	{
		_timerPaused = true;
		_timer.Stop();
	}

	public void ResumeTimer()
	{
		_timerPaused = false;
		if (IsTracking)
		{
			_timer.Start();
			OnTimerTick(null, EventArgs.Empty);
		}
	}

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
		_timer.Tick -= OnTimerTick;
		_worklogStateService.ActiveWorkChanged -= OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified -= OnWorkEntriesModified;
		Pomodoro.Dispose();
		App.ThemeChanged -= OnThemeChanged;
	}
}