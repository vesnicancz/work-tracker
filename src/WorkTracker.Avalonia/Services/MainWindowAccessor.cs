using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Holds no state of its own, so the application and the main window can use one before the services exist.
/// </summary>
public sealed class MainWindowAccessor : IMainWindowAccessor
{
	private static IClassicDesktopStyleApplicationLifetime? Desktop =>
		global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

	public Window? MainWindow => Desktop?.MainWindow;

	public void BringForward()
	{
		if (MainWindow is { } window)
		{
			window.Show();
			window.WindowState = WindowState.Normal;
			window.Activate();
		}
	}

	public void RequestShutdown()
	{
		Desktop?.Shutdown();
	}
}
