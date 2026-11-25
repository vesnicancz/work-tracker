using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow()
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
			if (DataContext is SettingsViewModel vm)
			{
				vm.CloseAction = () => Close(vm.DialogResult);
			}
		};

		// The titlebar X calls Close(false) directly, so Cancel() is not the only way out -
		// undo the live previews (language and theme) here to cover every dismissal path.
		Closed += (_, _) =>
		{
			if (DataContext is SettingsViewModel { DialogResult: false } vm)
			{
				vm.RevertPreview();
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

	private async void OnCopyConnectionDetailClick(object? sender, RoutedEventArgs e)
	{
		if (Clipboard is not { } clipboard
			|| sender is not Button { DataContext: PluginConnectionDetail detail, Content: MaterialIcon icon })
		{
			return;
		}

		try
		{
			await clipboard.SetTextAsync(detail.Value);
			icon.Kind = MaterialIconKind.Check;
		}
		catch (Exception)
		{
			// Another program can hold the clipboard, and an exception here would end the application.
			icon.Kind = MaterialIconKind.AlertCircleOutline;
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
