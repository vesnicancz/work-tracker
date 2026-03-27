using Avalonia;
using Avalonia.Labs.Notifications;

namespace WorkTracker.Avalonia;

class Program
{
	[STAThread]
	public static void Main(string[] args) => BuildAvaloniaApp()
		.StartWithClassicDesktopLifetime(args);

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
