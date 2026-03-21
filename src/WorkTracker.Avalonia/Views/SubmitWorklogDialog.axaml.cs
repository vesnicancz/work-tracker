using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

public partial class SubmitWorklogDialog : Window
{
	public SubmitWorklogDialog()
	{
		InitializeComponent();

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
