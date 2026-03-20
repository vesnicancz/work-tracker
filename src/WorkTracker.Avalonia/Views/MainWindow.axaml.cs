using Avalonia.Controls;
using Avalonia.Input;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Views;

public partial class MainWindow : Window
{
    private ITrayIconService? _trayIconService;
    private ISettingsService? _settingsService;

    public MainWindow()
    {
        InitializeComponent();

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        TitleBar.PointerPressed += OnTitleBarPointerPressed;

        Closing += OnWindowClosing;
    }

    public void Initialize(MainViewModel viewModel, ITrayIconService trayIconService, ISettingsService settingsService)
    {
        DataContext = viewModel;
        _trayIconService = trayIconService;
        _settingsService = settingsService;

        _trayIconService.Initialize();

        if (_settingsService.Settings.StartMinimized)
        {
            Hide();
            _trayIconService.Show();
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_settingsService?.Settings.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIconService?.Show();
        }
        else
        {
            _trayIconService?.Dispose();
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedWorkEntry is WorkEntry entry)
        {
            if (vm.EditWorkEntryCommand.CanExecute(entry))
            {
                vm.EditWorkEntryCommand.Execute(entry);
            }
        }
    }
}
