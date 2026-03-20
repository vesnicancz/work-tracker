using Avalonia.Controls;
using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Views;

/// <summary>
/// Modal dialog for reviewing and submitting worklogs to a configured upload provider.
/// </summary>
public partial class SubmitWorklogDialog : Window
{
    public SubmitWorklogDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is SubmitWorklogViewModel vm)
            {
                vm.CloseAction = () => Close(vm.DialogResult);
            }
        };
    }
}
