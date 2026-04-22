using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for the Suggestions dialog window.
/// Displays suggestion groups (one per plugin) with optional search.
/// </summary>
public class SuggestionsViewModel : ObservableObject, IDisposable
{
	private readonly IWorkSuggestionOrchestrator _orchestrator;
	private readonly IWorkSuggestionCache _cache;
	private readonly ISuggestionsViewState _viewState;
	private readonly TimeProvider _timeProvider;
	private readonly CancellationTokenSource _cts = new();
	private bool _isLoading;
	private DateTime _selectedDate;

	public SuggestionsViewModel(
		IWorkSuggestionOrchestrator orchestrator,
		IWorkSuggestionCache cache,
		ISuggestionsViewState viewState,
		TimeProvider timeProvider)
	{
		_orchestrator = orchestrator;
		_cache = cache;
		_viewState = viewState;
		_timeProvider = timeProvider;
		RefreshCommand = new AsyncRelayCommand(RefreshAsync);
		ToggleGroupCommand = new RelayCommand<SuggestionGroupViewModel>(ToggleGroup);
	}

	public ObservableCollection<SuggestionGroupViewModel> Groups { get; } = new();

	public bool IsLoading
	{
		get => _isLoading;
		set => SetProperty(ref _isLoading, value);
	}

	public IAsyncRelayCommand RefreshCommand { get; }
	public IRelayCommand<SuggestionGroupViewModel> ToggleGroupCommand { get; }

	/// <summary>
	/// Raised when a suggestion is selected by the user
	/// </summary>
	public event Action<WorkSuggestionViewModel>? SuggestionSelected;

	public async Task InitializeAsync(DateTime selectedDate)
	{
		_selectedDate = selectedDate;
		await LoadAsync();
	}

	private async Task RefreshAsync()
	{
		_cache.Invalidate();
		await LoadAsync();
	}

	private async Task LoadAsync()
	{
		try
		{
			IsLoading = true;
			var groups = await _orchestrator.GetGroupedSuggestionsAsync(_selectedDate, _cts.Token);

			foreach (var old in Groups)
			{
				old.Dispose();
			}
			Groups.Clear();
			foreach (var group in groups)
			{
				var groupVm = new SuggestionGroupViewModel(_orchestrator, group, _selectedDate, _timeProvider);
				groupVm.SuggestionSelected += suggestion => SuggestionSelected?.Invoke(suggestion);
				Groups.Add(groupVm);
			}

			var remembered = Groups.FirstOrDefault(g => g.PluginId == _viewState.LastExpandedPluginId);
			var initial = remembered ?? Groups.FirstOrDefault();
			if (initial != null)
			{
				initial.IsExpanded = true;
				_viewState.LastExpandedPluginId = initial.PluginId;
			}
		}
		catch (OperationCanceledException) { }
		finally
		{
			IsLoading = false;
		}
	}

	private void ToggleGroup(SuggestionGroupViewModel? target)
	{
		if (target == null)
		{
			return;
		}

		if (target.IsExpanded)
		{
			// Always keep at least one group expanded. Collapsing the active group
			// is only allowed when another group can take its place.
			var fallback = Groups.FirstOrDefault(g => g != target);
			if (fallback == null)
			{
				return;
			}
			target.IsExpanded = false;
			fallback.IsExpanded = true;
			_viewState.LastExpandedPluginId = fallback.PluginId;
			return;
		}

		foreach (var group in Groups)
		{
			if (group != target)
			{
				group.IsExpanded = false;
			}
		}
		target.IsExpanded = true;
		_viewState.LastExpandedPluginId = target.PluginId;
	}

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
		foreach (var group in Groups)
		{
			group.Dispose();
		}
	}
}
