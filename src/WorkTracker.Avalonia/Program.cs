using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Notifications;
using WorkTracker.Avalonia.Services.Linux;

namespace WorkTracker.Avalonia;

class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
#if DEBUG
		// A Debug build must not share the installed application's data. Without this it writes to the
		// same database, settings file and logs as the release install — and a development run with an
		// empty plugins directory then saves its own emptiness over the real plugin configuration.
		// Setting it here rather than in launchSettings.json covers every way a Debug build gets started:
		// an IDE run configuration, dotnet run, or the built binary launched by hand. An explicit value
		// still wins, so DOTNET_ENVIRONMENT=Production is how a Debug build is pointed at real data.
		if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
		{
			Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
		}
#endif

		// OnExplicitShutdown: this is a tray-resident app whose main window may never be shown
		// (StartMinimized) and is only hidden when closed, so it is not in the lifetime's window
		// list. Under the default OnLastWindowClose a dialog opened from the tray menu would be
		// the only listed window and closing it would end the process. Every exit path calls
		// Shutdown() explicitly: the tray "Exit" item, MainWindow closing with
		// CloseWindowBehavior.Exit, and the unrecoverable-startup-error path in App.
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

		// Main loop has exited — tear down the host (plugins, DB, logging) here instead of
		// in a ShutdownRequested handler: an async handler cannot be awaited by the lifetime,
		// which previously left the app half-disposed when a window cancelled the shutdown.
		if (global::Avalonia.Application.Current is App app)
		{
			app.ShutdownCleanup(TimeSpan.FromSeconds(10));
		}
	}

	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			// WM_CLASS defaults to the entry assembly name, which matches no desktop entry. Pairing
			// it with the app id lets the window manager tie a window to the installed .desktop file
			// (its StartupWMClass) and show the app's real name and icon in the task bar.
			.With(new X11PlatformOptions { WmClass = DesktopEntry.AppId })
			.WithInterFont()
			// AppNotificationOptions must be provided — passing null causes NRE in Avalonia.Labs.Notifications v11.3.1
			.WithAppNotifications(new AppNotificationOptions
			{
				AppName = "WorkTracker",
				AppUserModelId = "Vesnicancz.WorkTracker"
			})
			.LogToTrace();
}
