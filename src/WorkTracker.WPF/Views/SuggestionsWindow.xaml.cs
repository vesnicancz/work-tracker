using System.Windows;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.WPF.Views;

public partial class SuggestionsWindow : Window
{
	public WorkSuggestionViewModel? SelectedSuggestion { get; private set; }

	public SuggestionsWindow()
	{
		InitializeComponent();
		CloseButton.Click += (_, _) => Close();
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
}
