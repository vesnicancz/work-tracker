using System.Windows;
using WorkTracker.WPF.ViewModels;

namespace WorkTracker.WPF.Views;

/// <summary>
/// Dialog for submitting worklogs to Tempo
/// </summary>
public partial class SubmitWorklogDialog : Window
{
	public SubmitWorklogDialog()
	{
		InitializeComponent();

		// Window control button handler
		CloseButton.Click += (s, e) => Close();

		// Setup close action when DataContext is set
		DataContextChanged += (s, e) =>
		{
			if (DataContext is SubmitWorklogViewModel viewModel)
			{
				viewModel.CloseAction = () =>
				{
					DialogResult = viewModel.DialogResult;
					Close();
				};
			}
		};
	}

	private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
		{
			DragMove();
		}
	}
}
