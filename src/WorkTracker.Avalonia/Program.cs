using Avalonia;
using Avalonia.Labs.Notifications;

namespace WorkTracker.Avalonia;

class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
			.WithInterFont()
			// AppNotificationOptions must be provided — passing null causes NRE in Avalonia.Labs.Notifications v11.3.1
			.WithAppNotifications(new AppNotificationOptions
			{
				AppName = "WorkTracker",
				AppUserModelId = "Vesnicancz.WorkTracker"
			})
			.LogToTrace();
}
