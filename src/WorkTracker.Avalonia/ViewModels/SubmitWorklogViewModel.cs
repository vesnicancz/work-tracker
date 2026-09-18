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

namespace WorkTracker.Avalonia.ViewModels;

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
	private bool _suppressSelectionSync;
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

		SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
		RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, CanRetryFailed);
		CancelCommand = new RelayCommand(Cancel);
		ResetCommand = new RelayCommand(ResetToOriginal);
		InvertSelectionCommand = new RelayCommand(InvertSelection);
		SelectAllCommand = new RelayCommand(SelectAll);

		_allProviders = _orchestrator.LoadAvailableProviders();

		// Every provider is listed, whatever the mode: the provider is the primary choice and the
		// mode follows it (modes a provider cannot do are disabled in the dialog). Filtering the
		// list by mode would hide the very provider the user is trying to switch to.
		AvailableProviders = new ObservableCollection<ProviderInfo>(_allProviders);
		RestorePersistedSelection();
	}

	/// <summary>
	/// Reopens the dialog on the provider that was used last, in the mode that was last used with
	/// <i>that</i> provider. Settings written before per-provider modes existed have no remembered
	/// provider, so they fall back to the old behavior: the global mode decides, and the first
	/// provider that supports it is selected.
	/// </summary>
	private void RestorePersistedSelection()
	{
		var settings = _settingsService.Settings;
		var globalMode = GlobalMode(settings);

		var provider = _allProviders.FirstOrDefault(p => p.Id == settings.LastSubmissionProviderId)
			?? _allProviders.FirstOrDefault(p => p.SupportedModes.HasFlag(globalMode))
			?? _allProviders.FirstOrDefault();

		_selectedMode = provider != null ? ResolveModeFor(provider) : globalMode;

		// Nothing to persist while restoring, and the provider's own mode is already applied.
		WithSuppressedSelectionSync(() => SelectedProvider = provider);
	}

	private static WorklogSubmissionMode GlobalMode(ApplicationSettings settings) =>
		settings.LastSubmissionMode.IsSingleMode() ? settings.LastSubmissionMode : WorklogSubmissionMode.Timed;

	/// <summary>
	/// The mode to show for <paramref name="provider"/>: its own remembered mode when it is still
	/// one the provider supports, otherwise the last global mode, otherwise any mode the provider
	/// does support.
	/// </summary>
	private WorklogSubmissionMode ResolveModeFor(ProviderInfo provider)
	{
		var settings = _settingsService.Settings;

		if (settings.SubmissionModeByProvider.TryGetValue(provider.Id, out var remembered) &&
			remembered.IsSingleMode() &&
			provider.SupportedModes.HasFlag(remembered))
		{
			return remembered;
		}

		var globalMode = GlobalMode(settings);
		if (provider.SupportedModes.HasFlag(globalMode))
		{
			return globalMode;
		}

		if (provider.SupportedModes.HasFlag(WorklogSubmissionMode.Timed))
		{
			return WorklogSubmissionMode.Timed;
		}

		return provider.SupportedModes.HasFlag(WorklogSubmissionMode.Aggregated)
			? WorklogSubmissionMode.Aggregated
			: globalMode;
	}

	private void WithSuppressedSelectionSync(Action action)
	{
		_suppressSelectionSync = true;
		try
		{
			action();
		}
		finally
		{
			_suppressSelectionSync = false;
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
			if (!SetProperty(ref _selectedProvider, value))
			{
				return;
			}

			OnPropertyChanged(nameof(CanUseTimedMode));
			OnPropertyChanged(nameof(CanUseAggregatedMode));
			SendCommand.NotifyCanExecuteChanged();
			RetryFailedCommand.NotifyCanExecuteChanged();

			// The selection made while restoring must not rewrite settings; its mode is already set.
			if (_suppressSelectionSync || value == null)
			{
				return;
			}

			var mode = ResolveModeFor(value);
			if (mode != _selectedMode)
			{
				ApplyMode(mode);
			}

			PersistSelection(value.Id, _selectedMode);
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
			if (_selectedMode == value)
			{
				return;
			}

			// The dialog disables a mode the selected provider cannot do, so this only guards
			// against a programmatic set. Re-notify so a radio button that moved on its own snaps
			// back to the mode that is actually in effect.
			if (SelectedProvider != null && !SelectedProvider.SupportedModes.HasFlag(value))
			{
				OnPropertyChanged(nameof(IsTimedMode));
				OnPropertyChanged(nameof(IsAggregatedMode));
				return;
			}

			ApplyMode(value);
			PersistSelection(SelectedProvider?.Id, value);
		}
	}

	private void ApplyMode(WorklogSubmissionMode mode)
	{
		_selectedMode = mode;
		OnPropertyChanged(nameof(SelectedMode));
		OnPropertyChanged(nameof(IsTimedMode));
		OnPropertyChanged(nameof(IsAggregatedMode));

		_ = LoadPreviewAsync();
	}

	/// <summary>
	/// Whether the selected provider can submit in <paramref name="mode"/>. With no provider
	/// selected nothing is ruled out yet, so both modes stay available.
	/// </summary>
	private bool SupportsMode(WorklogSubmissionMode mode) =>
		SelectedProvider?.SupportedModes.HasFlag(mode) ?? true;

	/// <summary>
	/// Stores the dialog's current choice: the provider to reopen on, the mode it was last used
	/// with, and that same mode as the global fallback for providers not seen before.
	/// </summary>
	private void PersistSelection(string? providerId, WorklogSubmissionMode mode)
	{
		var settings = _settingsService.Settings;
		settings.LastSubmissionMode = mode;

		if (!string.IsNullOrEmpty(providerId))
		{
			settings.LastSubmissionProviderId = providerId;
			settings.SubmissionModeByProvider[providerId] = mode;
		}

		_ = PersistSettingsAsync(settings);
	}

	private async Task PersistSettingsAsync(ApplicationSettings settings)
	{
		try
		{
			await _settingsService.SaveSettingsAsync(settings);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to persist the submit dialog selection");
		}
	}

	/// <summary>
	/// Whether the Timed radio button is available for the selected provider.
	/// </summary>
	public bool CanUseTimedMode => SupportsMode(WorklogSubmissionMode.Timed);

	/// <summary>
	/// Whether the Aggregated radio button is available for the selected provider.
	/// </summary>
	public bool CanUseAggregatedMode => SupportsMode(WorklogSubmissionMode.Aggregated);

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
		// overwrite PreviewItems with stale results. Do NOT dispose the old CTS here — the
		// previous load's token is still in use inside the orchestrator/plugin pipeline, and
		// disposing could surface ObjectDisposedException. GC will clean it up once the last
		// task using it completes.
		var previousCts = _loadPreviewCts;
		_loadPreviewCts = new CancellationTokenSource();
		previousCts?.Cancel();
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

			// Covers the case where nothing in the dialog was touched: the selection restored on
			// open was never written back, and submitting confirms it is the one to remember.
			PersistSelection(SelectedProvider.Id, _selectedMode);

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
