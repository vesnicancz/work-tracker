using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.DTOs;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.WPF.ViewModels;

/// <summary>
/// ViewModel for submitting worklogs to upload providers
/// </summary>
public class SubmitWorklogViewModel : ViewModelBase, IDisposable
{
	private readonly IWorklogSubmissionOrchestrator _orchestrator;
	private readonly TimeProvider _timeProvider;
	private readonly ILocalizationService _localization;
	private readonly ISettingsService _settingsService;
	private readonly ILogger<SubmitWorklogViewModel> _logger;
	private readonly List<ProviderInfo> _allProviders;
	private bool _suppressRecalculation;
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
	private WorklogSubmissionMode _selectedMode;
	private CancellationTokenSource? _loadPreviewCts;

	public SubmitWorklogViewModel(
		IWorklogSubmissionOrchestrator orchestrator,
		ILocalizationService localization,
		ISettingsService settingsService,
		TimeProvider timeProvider,
		ILogger<SubmitWorklogViewModel> logger)
	{
		_orchestrator = orchestrator;
		_localization = localization;
		_settingsService = settingsService;
		_timeProvider = timeProvider;
		_logger = logger;
		_selectedDate = _timeProvider.GetLocalNow().Date;
		var persistedMode = _settingsService.Settings.LastSubmissionMode;
		_selectedMode = persistedMode.IsSingleMode() ? persistedMode : WorklogSubmissionMode.Timed;

		SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
		RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, CanRetryFailed);
		CancelCommand = new RelayCommand(Cancel);
		ResetCommand = new RelayCommand(ResetToOriginal);
		InvertSelectionCommand = new RelayCommand(InvertSelection);
		SelectAllCommand = new RelayCommand(SelectAll);

		_allProviders = _orchestrator.LoadAvailableProviders();
		RefreshProviderFilter();
	}

	private void RefreshProviderFilter()
	{
		var previousId = SelectedProvider?.Id;
		var filtered = _allProviders.Where(p => p.SupportedModes.HasFlag(_selectedMode)).ToList();
		AvailableProviders = new ObservableCollection<ProviderInfo>(filtered);

		SelectedProvider = filtered.FirstOrDefault(p => p.Id == previousId) ?? filtered.FirstOrDefault();
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

	public WorklogSubmissionMode SelectedMode
	{
		get => _selectedMode;
		set
		{
			if (SetProperty(ref _selectedMode, value))
			{
				OnPropertyChanged(nameof(IsTimedMode));
				OnPropertyChanged(nameof(IsAggregatedMode));

				var settings = _settingsService.Settings;
				settings.LastSubmissionMode = value;
				_ = PersistSettingsAsync(settings);

				RefreshProviderFilter();
				_ = LoadPreviewAsync();
			}
		}
	}

	private async Task PersistSettingsAsync(ApplicationSettings settings)
	{
		try
		{
			await _settingsService.SaveSettingsAsync(settings);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to persist LastSubmissionMode");
		}
	}

	public bool IsTimedMode
	{
		get => _selectedMode == WorklogSubmissionMode.Timed;
		set
		{
			if (value)
			{
				SelectedMode = WorklogSubmissionMode.Timed;
			}
		}
	}

	public bool IsAggregatedMode
	{
		get => _selectedMode == WorklogSubmissionMode.Aggregated;
		set
		{
			if (value)
			{
				SelectedMode = WorklogSubmissionMode.Aggregated;
			}
		}
	}

	public Action? CloseAction { get; set; }
	public bool DialogResult { get; set; }

	#endregion Properties

	#region Commands

	public IAsyncRelayCommand SendCommand { get; }
	public IAsyncRelayCommand RetryFailedCommand { get; }
	public ICommand CancelCommand { get; }
	public ICommand ResetCommand { get; }
	public ICommand InvertSelectionCommand { get; }
	public ICommand SelectAllCommand { get; }

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
		// Cancel any previous in-flight load so fast toggles of mode/week/date can't race and
		// overwrite PreviewItems with stale results.
		_loadPreviewCts?.Cancel();
		_loadPreviewCts?.Dispose();
		_loadPreviewCts = new CancellationTokenSource();
		var cancellationToken = _loadPreviewCts.Token;

		try
		{
			IsLoading = true;
			HasFailedItems = false;
			StatusMessage = _localization["LoadingPreview"];

			var result = await _orchestrator.LoadPreviewAsync(SelectedDate, IsWeekly, _selectedMode, _localization["NoTicket"], cancellationToken);

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

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
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Superseded by a newer LoadPreviewAsync call — silently drop the stale result.
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load worklog preview");
			StatusMessage = _localization.GetFormattedString("ErrorLoadingPreview", ex.Message);
			PreviewItems.Clear();
		}
		finally
		{
			if (!cancellationToken.IsCancellationRequested)
			{
				IsLoading = false;
			}
		}
	}

	private bool CanSend() => !IsSending && !IsLoading && PreviewItems.Any(i => !i.IsDateHeader && i.IsSelected) && SelectedProvider != null;

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

			var outcome = await _orchestrator.SubmitAsync(PreviewItems, SelectedProvider.Id, SelectedProvider.Name, _selectedMode, CancellationToken.None);
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

	private bool CanRetryFailed() => !IsSending && !IsLoading && HasFailedItems && PreviewItems.Any(i => !i.IsDateHeader && i.HasError && i.IsSelected) && SelectedProvider != null;

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

			var outcome = await _orchestrator.RetryFailedAsync(PreviewItems, SelectedProvider.Id, SelectedProvider.Name, _selectedMode, CancellationToken.None);
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

	public void Dispose()
	{
		_loadPreviewCts?.Cancel();
		_loadPreviewCts?.Dispose();
		_loadPreviewCts = null;
	}

	private void ResetToOriginal()
	{
		WithSuppressedRecalculation(() => _orchestrator.ResetItems(PreviewItems));
		HasFailedItems = false;
		RecalculateTotals();
		StatusMessage = _localization["WorklogsResetToOriginal"];
	}

	private void InvertSelection()
	{
		WithSuppressedRecalculation(() => _orchestrator.InvertSelection(PreviewItems));
		RecalculateTotals();
	}

	private void SelectAll()
	{
		WithSuppressedRecalculation(() => _orchestrator.SelectAll(PreviewItems));
		RecalculateTotals();
	}

	private void WithSuppressedRecalculation(Action action)
	{
		_suppressRecalculation = true;
		try
		{
			action();
		}
		finally
		{
			_suppressRecalculation = false;
		}
	}

	private void RecalculateTotals()
	{
		var selectedItems = PreviewItems.Where(i => !i.IsDateHeader && i.IsSelected);
		TotalTimeDisplay = _orchestrator.FormatDuration(selectedItems.Sum(i => i.Duration));
		StatusMessage = _localization.GetFormattedString("ReadyToSubmit", selectedItems.Count());
		SendCommand.NotifyCanExecuteChanged();
		RetryFailedCommand.NotifyCanExecuteChanged();
	}

	private void OnWorklogItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (!_suppressRecalculation &&
			e.PropertyName is nameof(WorklogPreviewItem.Duration) or nameof(WorklogPreviewItem.IsSelected))
		{
			RecalculateTotals();
		}
	}
}
