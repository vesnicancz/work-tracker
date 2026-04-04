using System.Windows;
using System.Windows.Threading;
using WorkTracker.WPF.ViewModels;

namespace WorkTracker.WPF.Views;

/// <summary>
/// Dialog for submitting worklogs to Tempo
/// </summary>
public partial class SubmitWorklogDialog : Window
{
	// Debounce: single-click delays 250ms so double-click can cancel it and run select-all instead
	private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

	public SubmitWorklogDialog()
	{
		InitializeComponent();
		_clickTimer.Tick += OnClickTimerTick;

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

	private void SelectionHeader_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (DataContext is SubmitWorklogViewModel vm)
		{
			_clickTimer.Stop();
			if (e.ClickCount >= 2)
			{
				vm.SelectAllCommand.Execute(null);
			}
			else
			{
				_clickTimer.Start();
			}
		}
		e.Handled = true;
	}

	private void OnClickTimerTick(object? sender, EventArgs e)
	{
		_clickTimer.Stop();
		if (DataContext is SubmitWorklogViewModel vm)
		{
			vm.InvertSelectionCommand.Execute(null);
		}
	}
}
