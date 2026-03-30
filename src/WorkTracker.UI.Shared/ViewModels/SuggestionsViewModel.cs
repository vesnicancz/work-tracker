using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for the Suggestions dialog window.
/// Displays suggestion groups (one per plugin) with optional search.
/// </summary>
public class SuggestionsViewModel : ObservableObject, IDisposable
{
	private readonly IWorkSuggestionOrchestrator _orchestrator;
	private readonly CancellationTokenSource _cts = new();
	private bool _isLoading;
	private DateTime _selectedDate;

	public SuggestionsViewModel(IWorkSuggestionOrchestrator orchestrator)
	{
		_orchestrator = orchestrator;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
	}

	public ObservableCollection<SuggestionGroupViewModel> Groups { get; } = new();

	public bool IsLoading
	{
		get => _isLoading;
		set => SetProperty(ref _isLoading, value);
	}

	public IAsyncRelayCommand RefreshCommand { get; }

	/// <summary>
	/// Raised when a suggestion is selected by the user
	/// </summary>
	public event Action<WorkSuggestionViewModel>? SuggestionSelected;

	public async Task InitializeAsync(DateTime selectedDate)
	{
		_selectedDate = selectedDate;
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
				var groupVm = new SuggestionGroupViewModel(_orchestrator, group, _selectedDate);
				groupVm.SuggestionSelected += suggestion => SuggestionSelected?.Invoke(suggestion);
				Groups.Add(groupVm);
			}
		}
		catch (OperationCanceledException) { }
		finally
		{
			IsLoading = false;
		}
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
