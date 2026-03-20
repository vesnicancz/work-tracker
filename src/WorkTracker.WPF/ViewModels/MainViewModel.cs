using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.WPF.Commands;
using WorkTracker.WPF.Services;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// Main ViewModel for the WorkTracker application
/// Handles work tracking, timer updates, and work entry management
/// </summary>
public class MainViewModel : ViewModelBase
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IDialogService _dialogService;
	private readonly INotificationService _notificationService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly ILogger<MainViewModel> _logger;
	private readonly DispatcherTimer _timer;
	private readonly Regex _jiraPattern = new Regex(@"^([a-zA-Z0-9]+-[0-9]+)", RegexOptions.Compiled);

	private string _elapsedTime = "00:00:00";

	// Input fields
	private string _workInput = string.Empty;

	private string? _detectedTicketId;
	private string? _detectedDescription;

	// Work entries list
	private ObservableCollection<WorkEntry> _workEntries = new();

	private DateTime _selectedDate = DateTime.Today;
	private WorkEntry? _selectedWorkEntry;

	// Total time for the selected day
	private string _totalDayDuration = "00:00:00";

	public MainViewModel(
		IServiceScopeFactory scopeFactory,
		IDialogService dialogService,
		INotificationService notificationService,
		IWorklogStateService worklogStateService,
		ILogger<MainViewModel> logger)
	{
		_scopeFactory = scopeFactory;
		_dialogService = dialogService;
		_notificationService = notificationService;
		_worklogStateService = worklogStateService;
		_logger = logger;

		// Subscribe to state change events
		_worklogStateService.ActiveWorkChanged += OnActiveWorkChanged;
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;
		_worklogStateService.WorkEntriesModified += OnWorkEntriesModified;

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

	public string ActiveTicketDisplay => ActiveWork?.TicketId ?? LocalizationService.Instance["NoTicket"];
	public string ActiveDescriptionDisplay => ActiveWork?.Description ?? LocalizationService.Instance["NoDescription"];

	public string TotalDayDuration
	{
		get => _totalDayDuration;
		set => SetProperty(ref _totalDayDuration, value);
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
			_notificationService.ShowError(LocalizationService.Instance["FailedToLoadWorkEntries"]);
		}
	}

	#endregion Initialization

	#region Work Input Parsing

	/// <summary>
	/// Parses work input to detect Jira ticket ID and description
	/// Format: "PROJECT-123 Description text" or just "Description text"
	/// </summary>
	private void ParseWorkInput(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			DetectedTicketId = null;
			DetectedDescription = null;
			return;
		}

		var match = _jiraPattern.Match(input);
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
				DetectedDescription);

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to start work: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			// Clear input
			WorkInput = string.Empty;

			// Refresh list
			await RefreshWorkEntriesAsync();

			_notificationService.ShowSuccess(LocalizationService.Instance["WorkTrackingStarted"]);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error starting work");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToStartWork", ex.Message));
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
			var result = await _worklogStateService.StopTrackingAsync();

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to stop work: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			ElapsedTime = "00:00:00";

			// Refresh list
			await RefreshWorkEntriesAsync();

			_notificationService.ShowSuccess(LocalizationService.Instance["WorkTrackingStopped"]);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error stopping work");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToStopWork", ex.Message));
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

				// Refresh state in case a new active work was created
				await _worklogStateService.RefreshFromDatabaseAsync();

				_notificationService.ShowSuccess(LocalizationService.Instance["WorkEntryCreated"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to create work entry");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToCreateWorkEntry", ex.Message));
		}
	}

	private async Task EditWorkEntryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null) return;

		try
		{
			var result = await _dialogService.ShowEditWorkEntryDialogAsync(workEntry);
			if (result)
			{
				// Refresh active work in case we edited the active entry
				var wasActive = workEntry.Id == ActiveWork?.Id;

				await RefreshWorkEntriesAsync();

				// Refresh state from database
				await _worklogStateService.RefreshFromDatabaseAsync();

				if (wasActive && !IsTracking)
				{
					// The active work was stopped via edit
					ElapsedTime = "00:00:00";
				}

				_notificationService.ShowSuccess(LocalizationService.Instance["WorkEntryUpdated"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to edit work entry");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToEditWorkEntry", ex.Message));
		}
	}

	private async Task DeleteWorkEntryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null) return;

		try
		{
			var confirmed = await _dialogService.ShowConfirmationAsync(
				LocalizationService.Instance.GetFormattedString("ConfirmDeleteMessage",
					workEntry.TicketId ?? "N/A",
					workEntry.Description ?? "N/A",
					$"{workEntry.StartTime:HH:mm} - {workEntry.EndTime?.ToString("HH:mm") ?? "Active"}"),
				LocalizationService.Instance["ConfirmDelete"]);

			if (confirmed)
			{
				var wasActive = workEntry.Id == ActiveWork?.Id;

				var result = await _worklogStateService.DeleteWorkEntryAsync(workEntry.Id);

				if (result.IsFailure)
				{
					_logger.LogWarning("Failed to delete work entry: {Error}", result.Error);
					await _dialogService.ShowErrorAsync(result.Error);
					return;
				}

				// WorklogStateService automatically refreshes state and notifies
				// WorkEntriesModified event will trigger RefreshWorkEntriesAsync()
				// We just need to reset elapsed time if we deleted the active entry
				if (wasActive)
				{
					ElapsedTime = "00:00:00";
				}

				_notificationService.ShowSuccess(LocalizationService.Instance["WorkEntryDeleted"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to delete work entry");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToDeleteWorkEntry", ex.Message));
		}
	}

	private async Task StartWorkFromHistoryAsync(WorkEntry? workEntry)
	{
		if (workEntry == null) return;

		try
		{
			// Start new work with the same ticket and description
			var result = await _worklogStateService.StartTrackingAsync(
				workEntry.TicketId,
				workEntry.Description);

			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to restart work from history: {Error}", result.Error);
				await _dialogService.ShowErrorAsync(result.Error);
				return;
			}

			// Refresh list to show the new entry
			await RefreshWorkEntriesAsync();

			_notificationService.ShowSuccess(LocalizationService.Instance["WorkRestartedSuccessfully"]);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error restarting work from history");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToRestartWork", ex.Message));
		}
	}

	private async Task SubmitWorklogAsync()
	{
		try
		{
			var result = await _dialogService.ShowSubmitWorklogDialogAsync(SelectedDate, false);
			if (result)
			{
				_notificationService.ShowSuccess(LocalizationService.Instance["WorklogsSubmittedSuccessfully"]);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to submit worklogs");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToSubmitWorklogs", ex.Message));
		}
	}

	private async Task OpenSettingsAsync()
	{
		try
		{
			await _dialogService.ShowSettingsDialogAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to open settings");
			await _dialogService.ShowErrorAsync(LocalizationService.Instance.GetFormattedString("FailedToOpenSettings", ex.Message));
		}
	}

	private async Task RefreshWorkEntriesAsync()
	{
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();

			var entries = await workEntryService.GetWorkEntriesByDateAsync(SelectedDate);
			WorkEntries = new ObservableCollection<WorkEntry>(entries.OrderBy(e => e.StartTime));
			UpdateTotalDayDuration();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to refresh work entries");
			_notificationService.ShowError(LocalizationService.Instance["FailedToLoadWorkEntries"]);
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
				var elapsed = DateTime.Now - entry.StartTime;
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

	#endregion Command Implementations

	#region Timer

	private void OnTimerTick(object? sender, EventArgs e)
	{
		if (ActiveWork != null)
		{
			var elapsed = DateTime.Now - ActiveWork.StartTime;
			ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

			// Update total day duration to include the running time
			UpdateTotalDayDuration();
		}
	}

	#endregion Timer

	#region Event Handlers

	private async void OnWorkEntriesModified(object? sender, EventArgs e)
	{
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
		// Update timer state
		if (isTracking)
		{
			_timer.Start();
		}
		else
		{
			_timer.Stop();
		}

		// Notify UI that IsTracking property changed
		OnPropertyChanged(nameof(IsTracking));

		// Update commands
		((AsyncRelayCommand)StartWorkCommand).RaiseCanExecuteChanged();
		((AsyncRelayCommand)StopWorkCommand).RaiseCanExecuteChanged();

		_logger.LogDebug("IsTracking changed in ViewModel: {IsTracking}", isTracking);
	}

	#endregion Event Handlers
}