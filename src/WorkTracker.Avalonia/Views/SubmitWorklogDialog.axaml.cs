using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

public partial class SubmitWorklogDialog : Window
{
	// Debounce: single-click delays 250ms so double-click can cancel it and run select-all instead
	private readonly DispatcherTimer _clickTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

	public SubmitWorklogDialog()
	{
		InitializeComponent();
		_clickTimer.Tick += OnClickTimerTick;
		Closed += (_, _) => _clickTimer.Stop();

		CloseButton.Click += (_, _) => Close(false);
		DialogTitleBar.PointerPressed += OnDragPointerPressed;
		DialogBorder.PointerPressed += (_, e) =>
		{
			if (!DialogTitleBar.IsVisible && !IsInteractiveElement(e))
			{
				OnDragPointerPressed(null, e);
			}
		};

		DataContextChanged += (_, _) =>
		{
			if (DataContext is SubmitWorklogViewModel vm)
			{
				vm.CloseAction = () => Close(vm.DialogResult);
			}
		};
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}

	private void SelectionHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (DataContext is SubmitWorklogViewModel vm && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
			e.Handled = true;
		}
	}

	private void OnClickTimerTick(object? sender, EventArgs e)
	{
		_clickTimer.Stop();
		if (DataContext is SubmitWorklogViewModel vm)
		{
			vm.InvertSelectionCommand.Execute(null);
		}
	}

	private static bool IsInteractiveElement(PointerPressedEventArgs e)
	{
		var source = e.Source as Visual;
		while (source != null)
		{
			if (source is Button or TextBox or ComboBox or CheckBox or RadioButton
				or ListBox or TabItem or ToggleButton or ScrollBar)
			{
				return true;
			}

			source = source.GetVisualParent() as Visual;
		}
		return false;
	}
}
