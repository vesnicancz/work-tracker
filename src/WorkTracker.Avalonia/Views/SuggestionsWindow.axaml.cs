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
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}
}
