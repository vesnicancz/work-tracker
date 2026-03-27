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
			.WithAppNotifications(new AppNotificationOptions
			{
				AppName = "WorkTracker",
				AppUserModelId = "Vesnicancz.WorkTracker"
			})
			.LogToTrace();
}
