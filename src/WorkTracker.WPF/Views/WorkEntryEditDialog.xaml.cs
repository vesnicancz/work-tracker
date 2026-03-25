using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
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

		// Global Enter key handler — confirm dialog unless focus is in a multiline TextBox or open popup
		PreviewKeyDown += OnPreviewKeyDown;

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

	private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
		{
			return;
		}

		// Don't intercept Enter in multiline TextBoxes
		if (Keyboard.FocusedElement is TextBox textBox && textBox.AcceptsReturn)
		{
			return;
		}

		// Don't intercept Enter when a popup is open (DatePicker/TimePicker calendar)
		if (IsPopupOpen())
		{
			return;
		}

		if (DataContext is WorkEntryEditViewModel vm)
		{
			var command = (IAsyncRelayCommand)vm.SaveCommand;
			if (command.CanExecute(null))
			{
				e.Handled = true;
				await command.ExecuteAsync(null);
			}
		}
	}

	private bool IsPopupOpen()
	{
		return FindVisualChildren<Popup>(this).Any(p => p.IsOpen);
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(parent);
		for (var i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is T t)
			{
				yield return t;
			}

			foreach (var descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
		{
			DragMove();
		}
	}
}