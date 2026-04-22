using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for a single suggestion group (one plugin's results)
/// </summary>
public class SuggestionGroupViewModel : ObservableObject, IDisposable
{
	private const int MinSearchLength = 3;

	private readonly IWorkSuggestionOrchestrator _orchestrator;
	private readonly TimeProvider _timeProvider;
	private readonly DateTime _date;
	private string _searchText = string.Empty;
	private bool _isSearching;
	private bool _isExpanded;
	private CancellationTokenSource? _searchCts;

	public SuggestionGroupViewModel(
		IWorkSuggestionOrchestrator orchestrator,
		SuggestionGroup group,
		DateTime date,
		TimeProvider timeProvider)
	{
		_orchestrator = orchestrator;
		_timeProvider = timeProvider;
		PluginId = group.PluginId;
		_date = date;
		Name = group.PluginName;
		IconHint = group.IconHint;
		SupportsSearch = group.SupportsSearch;
		Count = group.Items.Count;
		Error = group.Error;
		MarkPastItems(group.Items);
		Items = new ObservableCollection<WorkSuggestionViewModel>(group.Items);
		SelectCommand = new RelayCommand<WorkSuggestionViewModel>(OnSuggestionSelected);
	}

	public string PluginId { get; }
	public string Name { get; }
	public string? IconHint { get; }
	public bool SupportsSearch { get; }
	public int Count { get; private set; }
	public string? Error { get; private set; }
	public bool HasError => Error != null;
	public bool IsEmpty => !HasError && Count == 0;
	public ObservableCollection<WorkSuggestionViewModel> Items { get; }

	public bool IsExpanded
	{
		get => _isExpanded;
		set => SetProperty(ref _isExpanded, value);
	}

	public bool IsSearching
	{
		get => _isSearching;
		set => SetProperty(ref _isSearching, value);
	}

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (SetProperty(ref _searchText, value))
			{
				_ = OnSearchTextChanged(value);
			}
		}
	}

	public IRelayCommand<WorkSuggestionViewModel> SelectCommand { get; }

	public event Action<WorkSuggestionViewModel>? SuggestionSelected;

	private async Task OnSearchTextChanged(string query)
	{
		_searchCts?.Cancel();
		_searchCts?.Dispose();
		_searchCts = new CancellationTokenSource();

		var trimmed = query.Trim();

		// Don't search for 1-2 characters, but do search for empty (= reset to default filter)
		if (trimmed.Length > 0 && trimmed.Length < MinSearchLength)
		{
			return;
		}

		try
		{
			// Debounce — wait before sending API request (skip for empty = reset)
			if (trimmed.Length > 0)
			{
				await Task.Delay(300, _searchCts.Token);
			}

			IsSearching = true;
			var results = await _orchestrator.SearchPluginAsync(PluginId, trimmed, _date, _searchCts.Token);
			ReplaceItems(results);
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			// Search failed — orchestrator already logs details, keep UI consistent
		}
		finally
		{
			IsSearching = false;
		}
	}

	private void ReplaceItems(IReadOnlyList<WorkSuggestionViewModel> items)
	{
		// Mark before adding so the UI reads the final IsPast value when the
		// item enters the bound collection (IsPast has no INotifyPropertyChanged).
		MarkPastItems(items);
		Items.Clear();
		foreach (var item in items)
		{
			Items.Add(item);
		}
		Count = Items.Count;
		Error = null;
		OnPropertyChanged(nameof(Count));
		OnPropertyChanged(nameof(Error));
		OnPropertyChanged(nameof(HasError));
		OnPropertyChanged(nameof(IsEmpty));
	}

	private void MarkPastItems(IEnumerable<WorkSuggestionViewModel> items)
	{
		// Only dim past events when viewing today — for past/future days the
		// notion of "already happened" is either universal or meaningless.
		// Always assign (don't early-return) so stale IsPast from a previous
		// cached view across midnight gets cleared.
		var now = _timeProvider.GetLocalNow().DateTime;
		var viewingToday = _date.Date == now.Date;
		foreach (var item in items)
		{
			if (!item.HasTimeSlot)
			{
				item.IsPast = false;
				continue;
			}
			if (!viewingToday)
			{
				item.IsPast = false;
				continue;
			}
			var cutoff = item.EndTime ?? item.StartTime!.Value;
			item.IsPast = cutoff <= now;
		}
	}

	private void OnSuggestionSelected(WorkSuggestionViewModel? suggestion)
	{
		if (suggestion != null)
		{
			SuggestionSelected?.Invoke(suggestion);
		}
	}

	public void Dispose()
	{
		_searchCts?.Cancel();
		_searchCts?.Dispose();
	}
}