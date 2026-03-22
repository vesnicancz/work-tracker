using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;
using WorkTracker.WPF.Services;
using WorkTracker.WPF.ViewModels;

namespace WorkTracker.WPF.Views;

/// <summary>
/// Main window code-behind
/// Uses MVVM pattern - logic is in MainViewModel
/// </summary>
public partial class MainWindow : Window
{
	private readonly ITrayIconService _trayIconService;
	private readonly ISettingsService _settingsService;
	private readonly IWorklogStateService _worklogStateService;

	public MainWindow(MainViewModel viewModel, ITrayIconService trayIconService, ISettingsService settingsService, ISnackbarMessageQueue messageQueue, IWorklogStateService worklogStateService)
	{
		InitializeComponent();
		DataContext = viewModel;
		_trayIconService = trayIconService;
		_settingsService = settingsService;
		_worklogStateService = worklogStateService;

		// Set initial window icon and subscribe to tracking changes
		Icon = AppIconProvider.GetIcon(false);
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;

		// Bind the shared MessageQueue to the Snackbar
		MainSnackbar.MessageQueue = messageQueue as SnackbarMessageQueue;

		// Initialize tray icon
		_trayIconService.Initialize();

		// Handle window state changes
		StateChanged += OnStateChanged;
		Closing += OnClosing;
		Loaded += OnLoaded;

		// Window control button handler - behavior depends on settings
		CloseButton.Click += (s, e) =>
		{
			if (_settingsService.Settings.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray)
			{
				WindowState = WindowState.Minimized;
			}
			else
			{
				System.Windows.Application.Current.Shutdown();
			}
		};
	}

	private void OnLoaded(object? sender, RoutedEventArgs e)
	{
		// Check if application should start minimized
		if (_settingsService.Settings.StartMinimized)
		{
			WindowState = WindowState.Minimized;
		}
	}

	private void OnStateChanged(object? sender, System.EventArgs e)
	{
		if (WindowState == WindowState.Minimized)
		{
			// Only hide to tray if setting is MinimizeToTray
			if (_settingsService.Settings.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray)
			{
				Hide();
				_trayIconService.Show();
			}
		}
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		// Check settings for close behavior
		if (_settingsService.Settings.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray)
		{
			// Cancel the close event and minimize to tray instead
			e.Cancel = true;
			WindowState = WindowState.Minimized;
		}
		else
		{
			_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
			// Cleanup tray icon before exit
			_trayIconService.Dispose();
		}
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		var icon = AppIconProvider.GetIcon(isTracking);
		if (icon != null)
		{
			Icon = icon;
		}
	}

	private void TitleBar_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		// Prevent maximize on double-click
		e.Handled = true;
	}
}
