using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using WorkTracker.Avalonia.Services;
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
		// Subscribed here rather than in OnOpened: the window is shown and hidden repeatedly
		// (minimize to tray), and OnOpened runs on every show, which would stack handlers.
		PropertyChanged += OnWindowPropertyChanged;
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		// Only resume timer if window is actually visible (skip when starting minimized)
		if (IsVisible)
		{
			(DataContext as MainViewModel)?.ResumeTimer();
		}
	}

	public void Initialize(MainViewModel viewModel, ITrayIconService trayIconService, ISettingsService settingsService)
	{
		DataContext = viewModel;
		_trayIconService = trayIconService;
		_settingsService = settingsService;

		// Swap loading indicator for main content
		LoadingPanel.IsVisible = false;
		MainContent.IsVisible = true;

		// Set application window icon
		Icon = AppIconProvider.GetIcon() ?? Icon;

		_trayIconService.Initialize();

		if (_settingsService.Settings.StartMinimized)
		{
			viewModel.PauseTimer();
			_trayIconService.Show();
		}
	}

	private void OnWindowPropertyChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == IsVisibleProperty && e.NewValue is true)
		{
			(DataContext as MainViewModel)?.ResumeTimer();
		}
	}

	/// <summary>
	/// Sends the window to the tray. It is hidden rather than minimized, so it leaves no entry in the
	/// task bar — having one there with no window on screen is exactly what sending the app to the
	/// tray is meant to avoid. The application keeps running and tracking; only the display timer
	/// stops, and it resumes when the window is shown again.
	/// </summary>
	public void HideToTray()
	{
		(DataContext as MainViewModel)?.PauseTimer();
		Hide();
		_trayIconService?.Show();
	}

	/// <summary>
	/// Minimize-to-tray must only intercept user-initiated closes; cancelling a close with reason
	/// ApplicationShutdown/OSShutdown aborts the whole shutdown and blocks OS power-off on Linux
	/// (session manager keeps waiting for the app to exit).
	/// </summary>
	internal static bool ShouldMinimizeToTray(WindowCloseReason reason, CloseWindowBehavior behavior)
		=> reason == WindowCloseReason.WindowClosing && behavior == CloseWindowBehavior.MinimizeToTray;

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_settingsService != null
			&& ShouldMinimizeToTray(e.CloseReason, _settingsService.Settings.CloseWindowBehavior))
		{
			e.Cancel = true;
			HideToTray();
			return;
		}

		(DataContext as IDisposable)?.Dispose();
		_trayIconService?.Dispose();

		// The lifetime runs in OnExplicitShutdown, so closing the last window no longer ends the
		// application — request it here. Only for user-initiated closes: ApplicationShutdown and
		// OSShutdown mean a shutdown is already running and re-entering it would double-dispose.
		// Posted rather than called inline: Shutdown() closes every window in the lifetime's list,
		// including this one, and Window.CloseCore has no re-entrancy guard.
		if (e.CloseReason == WindowCloseReason.WindowClosing
			&& global::Avalonia.Application.Current?.ApplicationLifetime
				is IClassicDesktopStyleApplicationLifetime desktop)
		{
			Dispatcher.UIThread.Post(() => desktop.Shutdown());
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
