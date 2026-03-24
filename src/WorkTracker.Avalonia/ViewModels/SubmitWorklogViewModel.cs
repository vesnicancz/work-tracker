using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Services;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// ViewModel for submitting worklogs to upload providers
/// </summary>
public class SubmitWorklogViewModel : ViewModelBase
{
	private readonly IWorklogSubmissionService _submissionService;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ILogger<SubmitWorklogViewModel> _logger;

	private DateTime _selectedDate;
	private bool _isWeekly;
	private bool _isLoading;
	private bool _isSending;
	private string _statusMessage = string.Empty;
	private ObservableCollection<WorklogPreviewItem> _previewItems = new();
	private string _totalTimeDisplay = string.Empty;
	private ObservableCollection<Application.DTOs.ProviderInfo> _availableProviders = new();
	private Application.DTOs.ProviderInfo? _selectedProvider;
	private bool _hasFailedItems;

	public SubmitWorklogViewModel(
		IWorklogSubmissionService submissionService,
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<SubmitWorklogViewModel> logger)
	{
		_submissionService = submissionService;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;

		SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
		RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, CanRetryFailed);
		CancelCommand = new RelayCommand(Cancel);
		ResetCommand = new RelayCommand(ResetToOriginal);

		// Load available providers
		LoadAvailableProviders();
	}

	#region Properties

	public DateTime SelectedDate
	{
		get => _selectedDate;
		set
		{
			if (SetProperty(ref _selectedDate, value))
			{
				_ = LoadPreviewAsync();
			}
		}
	}

	public bool IsWeekly
	{
		get => _isWeekly;
		set
		{
			if (SetProperty(ref _isWeekly, value))
			{
				_ = LoadPreviewAsync();
			}
		}
	}

	public bool IsLoading
	{
		get => _isLoading;
		set => SetProperty(ref _isLoading, value);
	}

	public bool IsSending
	{
		get => _isSending;
		set
		{
			if (SetProperty(ref _isSending, value))
			{
				SendCommand.NotifyCanExecuteChanged();
				RetryFailedCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string StatusMessage
	{
		get => _statusMessage;
		set => SetProperty(ref _statusMessage, value);
	}

	public ObservableCollection<WorklogPreviewItem> PreviewItems
	{
		get => _previewItems;
		set => SetProperty(ref _previewItems, value);
	}

	public string TotalTimeDisplay
	{
		get => _totalTimeDisplay;
		set => SetProperty(ref _totalTimeDisplay, value);
	}

	public ObservableCollection<Application.DTOs.ProviderInfo> AvailableProviders
	{
		get => _availableProviders;
		set => SetProperty(ref _availableProviders, value);
	}

	public Application.DTOs.ProviderInfo? SelectedProvider
	{
		get => _selectedProvider;
		set
		{
			if (SetProperty(ref _selectedProvider, value))
			{
				SendCommand.NotifyCanExecuteChanged();
				RetryFailedCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public bool HasFailedItems
	{
		get => _hasFailedItems;
		private set
		{
			if (SetProperty(ref _hasFailedItems, value))
			{
				RetryFailedCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string DialogTitle => IsWeekly ? _localization["SubmitWeeklyWorklogs"] : _localization["SubmitDailyWorklogs"];

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	#endregion Properties

	#region Commands

	public IAsyncRelayCommand SendCommand { get; }
	public IAsyncRelayCommand RetryFailedCommand { get; }
	public ICommand CancelCommand { get; }
	public ICommand ResetCommand { get; }

	#endregion Commands

	#region Initialization

	public async Task InitializeAsync(DateTime? date, bool isWeek)
	{
		SelectedDate = date ?? _timeProvider.GetLocalNow().Date;
		IsWeekly = isWeek;
		await LoadPreviewAsync();
	}

	#endregion Initialization

	#region Preview Loading

	private async Task LoadPreviewAsync()
	{
		try
		{
			IsLoading = true;
			HasFailedItems = false;
			StatusMessage = _localization["LoadingPreview"];

			if (IsWeekly)
			{
				var weeklyPreview = await _submissionService.PreviewWeeklyWorklogAsync(SelectedDate);
				var items = new List<WorklogPreviewItem>();

				foreach (var dayPreview in weeklyPreview.OrderBy(kvp => kvp.Key))
				{
					items.Add(new WorklogPreviewItem
					{
						Date = dayPreview.Key,
						IsDateHeader = true,
						DateDisplay = dayPreview.Key.ToString("dddd, MMMM dd, yyyy")
					});

					foreach (var entry in dayPreview.Value.Worklogs)
					{
						var item = new WorklogPreviewItem
						{
							Date = dayPreview.Key,
							TicketId = entry.TicketId ?? _localization["NoTicket"],
							Description = entry.Description ?? string.Empty,
							Duration = entry.DurationMinutes * 60,
							StartTime = entry.StartTime,
							EndTime = entry.EndTime
						};
						item.SaveOriginalValues();
						items.Add(item);
					}
				}

				PreviewItems = new ObservableCollection<WorklogPreviewItem>(items);

				// Subscribe to property changes to update total time
				foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
				{
					item.PropertyChanged += OnWorklogItemPropertyChanged;
				}

				var totalSeconds = weeklyPreview.Sum(kvp => kvp.Value.Worklogs.Sum(w => w.DurationMinutes * 60));
				TotalTimeDisplay = FormatDuration(totalSeconds);
			}
			else
			{
				var dailyPreview = await _submissionService.PreviewDailyWorklogAsync(SelectedDate);
				var items = dailyPreview.Worklogs.Select(entry =>
				{
					var item = new WorklogPreviewItem
					{
						Date = SelectedDate,
						TicketId = entry.TicketId ?? _localization["NoTicket"],
						Description = entry.Description ?? string.Empty,
						Duration = entry.DurationMinutes * 60,
						StartTime = entry.StartTime,
						EndTime = entry.EndTime
					};
					item.SaveOriginalValues();
					return item;
				}).ToList();

				PreviewItems = new ObservableCollection<WorklogPreviewItem>(items);

				// Subscribe to property changes to update total time
				foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
				{
					item.PropertyChanged += OnWorklogItemPropertyChanged;
				}

				var totalSeconds = dailyPreview.Worklogs.Sum(w => w.DurationMinutes * 60);
				TotalTimeDisplay = FormatDuration(totalSeconds);
			}

			StatusMessage = _localization.GetFormattedString("ReadyToSubmit", PreviewItems.Count(i => !i.IsDateHeader));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load worklog preview");
			StatusMessage = _localization.GetFormattedString("ErrorLoadingPreview", ex.Message);
			PreviewItems.Clear();
		}
		finally
		{
			IsLoading = false;
		}
	}

	#endregion Preview Loading

	#region Command Implementations

	private bool CanSend()
	{
		return !IsSending && !IsLoading && PreviewItems.Any(i => !i.IsDateHeader) && SelectedProvider != null;
	}

	private async Task SendAsync()
	{
		if (SelectedProvider == null)
		{
			StatusMessage = _localization["PleaseSelectProvider"];
			return;
		}

		try
		{
			IsSending = true;
			StatusMessage = _localization.GetFormattedString("SubmittingTo", SelectedProvider.Name);

			// Convert edited preview items to DTOs
			var worklogs = PreviewItems
				.Where(i => !i.IsDateHeader)
				.Select(i => new Application.DTOs.WorklogDto
				{
					TicketId = i.TicketId,
					Description = i.Description,
					StartTime = i.StartTime,
					EndTime = i.EndTime,
					DurationMinutes = i.Duration / 60
				})
				.ToList();

			// Submit custom worklogs with edited values using plugin ID
			var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, SelectedProvider.Id);

			if (result.IsSuccess && result.Value != null)
			{
				var submission = result.Value;
				MarkFailedItems(submission);
				StatusMessage = FormatSubmissionStatus(submission, SelectedProvider.Name);

				if (submission.FailedEntries == 0)
				{
					DialogResult = true;
				}
			}
			else
			{
				StatusMessage = _localization.GetFormattedString("ErrorPrefix", result.Error);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to submit worklogs");
			StatusMessage = _localization.GetFormattedString("ErrorPrefix", ex.Message);
		}
		finally
		{
			IsSending = false;
		}
	}

	private bool CanRetryFailed()
	{
		return !IsSending && !IsLoading && HasFailedItems && SelectedProvider != null;
	}

	private async Task RetryFailedAsync()
	{
		if (SelectedProvider == null)
		{
			StatusMessage = _localization["PleaseSelectProvider"];
			return;
		}

		try
		{
			IsSending = true;
			StatusMessage = _localization["RetryingFailed"];

			var worklogs = PreviewItems
				.Where(i => !i.IsDateHeader && i.HasError)
				.Select(i => new Application.DTOs.WorklogDto
				{
					TicketId = i.TicketId,
					Description = i.Description,
					StartTime = i.StartTime,
					EndTime = i.EndTime,
					DurationMinutes = i.Duration / 60
				})
				.ToList();

			if (worklogs.Count == 0)
			{
				HasFailedItems = false;
				StatusMessage = string.Empty;
				IsSending = false;
				return;
			}

			var result = await _submissionService.SubmitCustomWorklogsAsync(worklogs, SelectedProvider.Id);

			if (result.IsSuccess && result.Value != null)
			{
				var submission = result.Value;
				MarkFailedItems(submission);
				StatusMessage = FormatSubmissionStatus(submission, SelectedProvider.Name);

				if (!HasFailedItems)
				{
					DialogResult = true;
				}
			}
			else
			{
				StatusMessage = _localization.GetFormattedString("ErrorPrefix", result.Error);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retry worklogs");
			StatusMessage = _localization.GetFormattedString("ErrorPrefix", ex.Message);
		}
		finally
		{
			IsSending = false;
		}
	}

	private void Cancel()
	{
		DialogResult = false;
		CloseAction?.Invoke();
	}

	private void ResetToOriginal()
	{
		foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
		{
			item.RestoreOriginalValues();
			item.HasError = false;
			item.ErrorMessage = null;
		}

		HasFailedItems = false;

		// Recalculate total time
		var totalSeconds = PreviewItems.Where(i => !i.IsDateHeader).Sum(i => i.Duration);
		TotalTimeDisplay = FormatDuration(totalSeconds);
		StatusMessage = _localization["WorklogsResetToOriginal"];
	}

	#endregion Command Implementations

	#region Provider Management

	private void LoadAvailableProviders()
	{
		try
		{
			var providers = _submissionService.GetAvailableProviders().ToList();
			AvailableProviders = new ObservableCollection<Application.DTOs.ProviderInfo>(providers);

			// Select first provider by default
			if (AvailableProviders.Any())
			{
				SelectedProvider = AvailableProviders.First();
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load available providers");
		}
	}

	#endregion Provider Management

	#region Helpers

	private string FormatDuration(int seconds)
	{
		var timeSpan = TimeSpan.FromSeconds(seconds);
		var hours = (int)timeSpan.TotalHours;
		var minutes = timeSpan.Minutes;

		if (hours > 0)
		{
			return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
		}

		return $"{minutes}m";
	}

	private void OnWorklogItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(WorklogPreviewItem.Duration))
		{
			// Recalculate total time
			var totalSeconds = PreviewItems.Where(i => !i.IsDateHeader).Sum(i => i.Duration);
			TotalTimeDisplay = FormatDuration(totalSeconds);
		}
	}

	private string FormatSubmissionStatus(Application.Common.SubmissionResult submission, string providerName)
	{
		if (submission.FailedEntries == 0)
		{
			return _localization.GetFormattedString("SubmissionSuccess", submission.SuccessfulEntries, providerName);
		}

		if (submission.SuccessfulEntries == 0)
		{
			return _localization.GetFormattedString("SubmissionAllFailed", submission.FailedEntries, providerName);
		}

		return _localization.GetFormattedString("SubmissionPartial", submission.SuccessfulEntries, providerName, submission.FailedEntries);
	}

	private void MarkFailedItems(Application.Common.SubmissionResult submission)
	{
		// Clear previous error state
		foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
		{
			item.HasError = false;
			item.ErrorMessage = null;
		}

		if (submission.Errors.Count == 0)
		{
			HasFailedItems = false;
			return;
		}

		// Match errors to preview items by TicketId + Date + time range (from Details)
		var dataItems = PreviewItems.Where(i => !i.IsDateHeader).ToList();
		foreach (var error in submission.Errors)
		{
			var match = dataItems.FirstOrDefault(i =>
				i.Date.Date == error.Date.Date &&
				(string.IsNullOrEmpty(error.TicketId) || i.TicketId == error.TicketId) &&
				error.Details == $"{i.StartTime:HH:mm}-{i.EndTime:HH:mm}");

			// Fallback: match by TicketId + Date only (ignore TicketId when error has none)
			match ??= dataItems.FirstOrDefault(i =>
				i.Date.Date == error.Date.Date &&
				(string.IsNullOrEmpty(error.TicketId) || i.TicketId == error.TicketId) &&
				!i.HasError);

			if (match != null)
			{
				match.HasError = true;
				match.ErrorMessage = error.ErrorMessage;
			}
		}

		HasFailedItems = PreviewItems.Any(i => i.HasError);
	}

	#endregion Helpers
}

/// <summary>
/// Preview item for worklog submission
/// </summary>
public class WorklogPreviewItem : ViewModelBase
{
	private string? _ticketId;
	private string? _description;
	private int _duration;
	private DateTime _startTime;
	private DateTime _endTime;

	// Cached display strings for performance
	private string _startTimeDisplay = string.Empty;
	private string _endTimeDisplay = string.Empty;
	private string _durationDisplay = string.Empty;

	// Error state
	private bool _hasError;
	private string? _errorMessage;

	// Original values for reset
	private string? _originalTicketId;
	private string? _originalDescription;
	private int _originalDuration;
	private DateTime _originalStartTime;
	private DateTime _originalEndTime;

	public DateTime Date { get; set; }

	public bool IsDateHeader { get; set; }

	public string? DateDisplay { get; set; }

	public bool HasError
	{
		get => _hasError;
		set => SetProperty(ref _hasError, value);
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		set => SetProperty(ref _errorMessage, value);
	}

	public string? TicketId
	{
		get => _ticketId;
		set => SetProperty(ref _ticketId, value);
	}

	public string? Description
	{
		get => _description;
		set => SetProperty(ref _description, value);
	}

	public int Duration
	{
		get => _duration;
		set
		{
			if (SetProperty(ref _duration, value))
			{
				UpdateDurationDisplayCache();
			}
		}
	}

	public DateTime StartTime
	{
		get => _startTime;
		set
		{
			if (SetProperty(ref _startTime, value))
			{
				UpdateStartTimeDisplayCache();
				UpdateDurationFromTimes();
			}
		}
	}

	public DateTime EndTime
	{
		get => _endTime;
		set
		{
			if (SetProperty(ref _endTime, value))
			{
				UpdateEndTimeDisplayCache();
				UpdateDurationFromTimes();
			}
		}
	}

	public string StartTimeDisplay
	{
		get => _startTimeDisplay;
		set
		{
			if (TimeSpan.TryParse(value, out var time))
			{
				StartTime = Date.Date.Add(time);
			}
		}
	}

	public string EndTimeDisplay
	{
		get => _endTimeDisplay;
		set
		{
			if (TimeSpan.TryParse(value, out var time))
			{
				EndTime = Date.Date.Add(time);
			}
		}
	}

	public string DurationDisplay => _durationDisplay;

	private void UpdateStartTimeDisplayCache()
	{
		_startTimeDisplay = StartTime.ToString("HH:mm");
		OnPropertyChanged(nameof(StartTimeDisplay));
	}

	private void UpdateEndTimeDisplayCache()
	{
		_endTimeDisplay = EndTime.ToString("HH:mm");
		OnPropertyChanged(nameof(EndTimeDisplay));
	}

	private void UpdateDurationDisplayCache()
	{
		var timeSpan = TimeSpan.FromSeconds(Duration);
		var hours = (int)timeSpan.TotalHours;
		var minutes = timeSpan.Minutes;

		if (hours > 0)
		{
			_durationDisplay = minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
		}
		else
		{
			_durationDisplay = $"{minutes}m";
		}

		OnPropertyChanged(nameof(DurationDisplay));
	}

	public void SaveOriginalValues()
	{
		_originalTicketId = TicketId;
		_originalDescription = Description;
		_originalDuration = Duration;
		_originalStartTime = StartTime;
		_originalEndTime = EndTime;
	}

	public void RestoreOriginalValues()
	{
		TicketId = _originalTicketId;
		Description = _originalDescription;
		Duration = _originalDuration;
		StartTime = _originalStartTime;
		EndTime = _originalEndTime;
	}

	private void UpdateDurationFromTimes()
	{
		if (EndTime > StartTime)
		{
			Duration = (int)(EndTime - StartTime).TotalSeconds;
		}
	}
}