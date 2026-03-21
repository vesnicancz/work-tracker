using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IDialogService _dialogService;
	private readonly INotificationService _notificationService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ILogger<MainViewModel> _logger;
	private readonly DispatcherTimer _timer;
	private readonly CancellationTokenSource _cts = new();
	private bool _disposed;
	private bool _timerPaused;

	private string _elapsedTime = "00:00:00";
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
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<MainViewModel> logger)
	{
		_scopeFactory = scopeFactory;
		_dialogService = dialogService;
		_notificationService = notificationService;
		_worklogStateService = worklogStateService;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;

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
		if (string.IsNullOrWhiteSpace(input))
		{
			DetectedTicketId = null;
			DetectedDescription = null;
			return;
		}

		var match = JiraPatterns.TicketId().Match(input);
		if (match.Success)
		{
			DetectedTicketId = match.Groups[1].Value;
			var remaining = input.Substring(DetectedTicketId.Length).TrimStart();
			DetectedDescription = string.IsNullOrWhiteSpace(remaining) ? null : remaining;
		}
		else
		{
			DetectedTicketId = null;
			DetectedDescription = input;
		}
	}

	private bool CanStartWork() => !string.IsNullOrWhiteSpace(WorkInput);

	private async Task StartWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StartTrackingAsync(DetectedTicketId, DetectedDescription);
			if (result.IsFailure)
			{
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}
			WorkInput = string.Empty;
			await RefreshWorkEntriesAsync();
			_notificationService.ShowSuccess(_localization["WorkTrackingStarted"]);
		}
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
			var result = await _worklogStateService.StopTrackingAsync();
			if (result.IsFailure)
			{
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}
			ElapsedTime = "00:00:00";
			await RefreshWorkEntriesAsync();
			_notificationService.ShowSuccess(_localization["WorkTrackingStopped"]);
		}
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
			var result = await _dialogService.ShowEditWorkEntryDialogAsync(null);
			if (result)
			{
				await RefreshWorkEntriesAsync();
				await _worklogStateService.RefreshFromDatabaseAsync();
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
				var wasActive = workEntry.Id == ActiveWork?.Id;
				await RefreshWorkEntriesAsync();
				await _worklogStateService.RefreshFromDatabaseAsync();
				if (wasActive && !IsTracking)
				{
					ElapsedTime = "00:00:00";
				}

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
				var wasActive = workEntry.Id == ActiveWork?.Id;
				var result = await _worklogStateService.DeleteWorkEntryAsync(workEntry.Id);
				if (result.IsFailure)
				{
					await _dialogService.ShowErrorAsync(result.Error);
					return;
				}
				if (wasActive)
				{
					ElapsedTime = "00:00:00";
				}

				_notificationService.ShowSuccess(_localization["WorkEntryDeleted"]);
			}
		}
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
			var result = await _worklogStateService.StartTrackingAsync(workEntry.TicketId, workEntry.Description);
			if (result.IsFailure)
			{
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}
			await RefreshWorkEntriesAsync();
			_notificationService.ShowSuccess(_localization["WorkRestartedSuccessfully"]);
		}
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
		try { await _dialogService.ShowSettingsDialogAsync(); }
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
		}

		OnPropertyChanged(nameof(IsTracking));
		StartWorkCommand.NotifyCanExecuteChanged();
		StopWorkCommand.NotifyCanExecuteChanged();
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
		_worklogStateService.ActiveWorkChanged -= OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified -= OnWorkEntriesModified;
	}
}