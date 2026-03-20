using System.Windows;
using WorkTracker.WPF.ViewModels;

namespace WorkTracker.WPF.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow()
	{
		InitializeComponent();

		// Window control button handler
		CloseButton.Click += (s, e) => Close();

		// Setup close action when DataContext is set
		DataContextChanged += (s, e) =>
		{
			if (DataContext is SettingsViewModel viewModel)
			{
				viewModel.CloseAction = () =>
				{
					DialogResult = viewModel.DialogResult;
					Close();
				};
			}
		};
	}
}
