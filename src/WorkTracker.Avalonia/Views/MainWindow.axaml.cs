using Avalonia.Controls;
using WorkTracker.Avalonia.ViewModels;
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
}
