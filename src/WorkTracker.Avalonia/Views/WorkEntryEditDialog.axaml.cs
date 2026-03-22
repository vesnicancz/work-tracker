using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

public partial class WorkEntryEditDialog : Window
{
	public WorkEntryEditDialog()
	{
		InitializeComponent();

		// Close button (visible when titlebar is shown)
		CloseButton.Click += (_, _) => Close(false);

		// Global Enter key handler — confirm dialog unless focus is in a multiline TextBox or open popup
		KeyDown += OnKeyDown;

		// Drag: titlebar when visible, whole border otherwise
		DialogTitleBar.PointerPressed += OnDragPointerPressed;
		DialogBorder.PointerPressed += (_, e) =>
		{
			// Only drag from border if titlebar is hidden (Purple theme)
			if (!DialogTitleBar.IsVisible)
			{
				OnDragPointerPressed(null, e);
			}
		};

		DataContextChanged += (_, _) =>
		{
			if (DataContext is WorkEntryEditViewModel vm)
			{
				vm.CloseAction = () => Close(vm.DialogResult);
			}
		};
	}

	private async void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
		{
			return;
		}

		// Don't intercept Enter in multiline TextBoxes
		if (FocusManager?.GetFocusedElement() is TextBox { AcceptsReturn: true })
		{
			return;
		}

		// Don't intercept Enter when a popup is open (DatePicker/TimePicker calendar)
		if (this.GetVisualDescendants().OfType<Popup>().Any(p => p.IsOpen))
		{
			return;
		}

		if (DataContext is WorkEntryEditViewModel vm && vm.SaveCommand.CanExecute(null))
		{
			e.Handled = true;
			await ((IAsyncRelayCommand)vm.SaveCommand).ExecuteAsync(null);
		}
	}

	private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}
}
