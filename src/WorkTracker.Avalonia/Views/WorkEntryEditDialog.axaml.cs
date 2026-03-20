using Avalonia.Controls;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Modal dialog for creating and editing work entries.
/// </summary>
public partial class WorkEntryEditDialog : Window
{
    public WorkEntryEditDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorkEntryEditViewModel vm)
            {
                vm.CloseAction = () => Close(vm.DialogResult);
            }
        };
    }
}
