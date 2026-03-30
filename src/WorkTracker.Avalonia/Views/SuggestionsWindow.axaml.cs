using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Views;

public partial class SuggestionsWindow : Window
{
	public WorkSuggestionViewModel? SelectedSuggestion { get; private set; }

	public SuggestionsWindow()
	{
		InitializeComponent();

		CloseButton.Click += (_, _) => Close();
		DialogTitleBar.PointerPressed += OnDragPointerPressed;
	}

	public void BindViewModel(SuggestionsViewModel viewModel)
	{
		DataContext = viewModel;
		viewModel.SuggestionSelected += suggestion =>
		{
			SelectedSuggestion = suggestion;
			Close();
		};

		viewModel.Groups.CollectionChanged += OnGroupsChanged;
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		UpdateItemsMaxHeight();
	}

	protected override void OnClosed(EventArgs e)
	{
		if (DataContext is SuggestionsViewModel vm)
		{
			vm.Groups.CollectionChanged -= OnGroupsChanged;
		}
		base.OnClosed(e);
	}

	private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		UpdateItemsMaxHeight();
	}

	private void UpdateItemsMaxHeight()
	{
		var clientHeight = ClientSize.Height;
		if (clientHeight <= 0)
		{
			return;
		}

		var groupCount = (DataContext as SuggestionsViewModel)?.Groups.Count ?? 1;
		var chrome = 16 + 2 + 36 + 24 + 28 + 34 + 40 + 16;
		var collapsedCards = (groupCount - 1) * 48;
		var available = clientHeight - chrome - collapsedCards;

		Resources["ItemsScrollMaxHeight"] = Math.Max(available, 100);
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}
}
