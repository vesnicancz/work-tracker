using Avalonia.Controls;
using Avalonia.Input;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

public partial class WorkEntryEditDialog : Window
{
    public WorkEntryEditDialog()
    {
        InitializeComponent();

        // Close button (visible when titlebar is shown)
        CloseButton.Click += (_, _) => Close(false);

        // Drag: titlebar when visible, whole border otherwise
        DialogTitleBar.PointerPressed += OnDragPointerPressed;
        DialogBorder.PointerPressed += (_, e) =>
        {
            // Only drag from border if titlebar is hidden (Purple theme)
            if (!DialogTitleBar.IsVisible)
                OnDragPointerPressed(null, e);
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorkEntryEditViewModel vm)
            {
                vm.CloseAction = () => Close(vm.DialogResult);
            }
        };
    }

    private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
