using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.DTOs;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// ViewModel for submitting worklogs to upload providers
/// </summary>
public class SubmitWorklogViewModel : ViewModelBase
{
	private readonly IWorklogSubmissionOrchestrator _orchestrator;
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
	private ObservableCollection<ProviderInfo> _availableProviders = new();
	private ProviderInfo? _selectedProvider;
	private bool _hasFailedItems;

	public SubmitWorklogViewModel(
		IWorklogSubmissionOrchestrator orchestrator,
		ILocalizationService localization,
		TimeProvider timeProvider,
		ILogger<SubmitWorklogViewModel> logger)
	{
		_orchestrator = orchestrator;
		_localization = localization;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;

		SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
		RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, CanRetryFailed);
		CancelCommand = new RelayCommand(Cancel);
		ResetCommand = new RelayCommand(ResetToOriginal);

		var providers = _orchestrator.LoadAvailableProviders();
		AvailableProviders = new ObservableCollection<ProviderInfo>(providers);
		if (AvailableProviders.Any())
		{
			SelectedProvider = AvailableProviders.First();
		}
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
		set
		{
			if (SetProperty(ref _isLoading, value))
			{
				SendCommand.NotifyCanExecuteChanged();
				RetryFailedCommand.NotifyCanExecuteChanged();
			}
		}
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

	public ObservableCollection<ProviderInfo> AvailableProviders
	{
		get => _availableProviders;
		set => SetProperty(ref _availableProviders, value);
	}

	public ProviderInfo? SelectedProvider
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

	public async Task InitializeAsync(DateTime? date, bool isWeek)
	{
		_selectedDate = date ?? _timeProvider.GetLocalNow().Date;
		OnPropertyChanged(nameof(SelectedDate));
		_isWeekly = isWeek;
		OnPropertyChanged(nameof(IsWeekly));
		OnPropertyChanged(nameof(DialogTitle));
		await LoadPreviewAsync();
	}

	private async Task LoadPreviewAsync()
	{
		try
		{
			IsLoading = true;
			HasFailedItems = false;
			StatusMessage = _localization["LoadingPreview"];

			var result = await _orchestrator.LoadPreviewAsync(SelectedDate, IsWeekly, _localization["NoTicket"], CancellationToken.None);

			// Unsubscribe from old items before replacing
			foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
			{
				item.PropertyChanged -= OnWorklogItemPropertyChanged;
			}

			PreviewItems = new ObservableCollection<WorklogPreviewItem>(result.Items);

			foreach (var item in PreviewItems.Where(i => !i.IsDateHeader))
			{
				item.PropertyChanged += OnWorklogItemPropertyChanged;
			}

			TotalTimeDisplay = _orchestrator.FormatDuration(result.TotalSeconds);
			StatusMessage = _localization.GetFormattedString("ReadyToSubmit", result.DataItemCount);
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

	private bool CanSend() => !IsSending && !IsLoading && PreviewItems.Any(i => !i.IsDateHeader) && SelectedProvider != null;

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

			var outcome = await _orchestrator.SubmitAsync(PreviewItems, SelectedProvider.Id, SelectedProvider.Name, CancellationToken.None);
			HasFailedItems = outcome.HasFailedItems;
			StatusMessage = outcome.StatusMessage;

			if (outcome.AllSucceeded)
			{
				DialogResult = true;
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

	private bool CanRetryFailed() => !IsSending && !IsLoading && HasFailedItems && SelectedProvider != null;

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

			var outcome = await _orchestrator.RetryFailedAsync(PreviewItems, SelectedProvider.Id, SelectedProvider.Name, CancellationToken.None);
			HasFailedItems = outcome.HasFailedItems;
			StatusMessage = outcome.StatusMessage;

			if (outcome.AllSucceeded)
			{
				DialogResult = true;
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
		_orchestrator.ResetItems(PreviewItems);
		HasFailedItems = false;

		var totalSeconds = PreviewItems.Where(i => !i.IsDateHeader).Sum(i => i.Duration);
		TotalTimeDisplay = _orchestrator.FormatDuration(totalSeconds);
		StatusMessage = _localization["WorklogsResetToOriginal"];
	}

	private void OnWorklogItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(WorklogPreviewItem.Duration))
		{
			var totalSeconds = PreviewItems.Where(i => !i.IsDateHeader).Sum(i => i.Duration);
			TotalTimeDisplay = _orchestrator.FormatDuration(totalSeconds);
		}
	}
}