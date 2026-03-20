using System.Windows;
using WorkTracker.WPF.ViewModels;

namespace WorkTracker.WPF.Views;

/// <summary>
/// Dialog for editing work entries
/// </summary>
public partial class WorkEntryEditDialog : Window
{
	public WorkEntryEditDialog()
	{
		InitializeComponent();

		// Window control button handler
		CloseButton.Click += (s, e) => Close();

		// Setup close action when DataContext is set
		DataContextChanged += (s, e) =>
		{
			if (DataContext is WorkEntryEditViewModel viewModel)
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
