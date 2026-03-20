using Avalonia.Controls;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Modal settings window with General, Plugins, and Favorites tabs.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.CloseAction = () => Close(vm.DialogResult);
            }
        };
    }
}
