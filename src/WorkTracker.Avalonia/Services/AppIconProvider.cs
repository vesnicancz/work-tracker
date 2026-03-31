using Avalonia.Controls;

namespace WorkTracker.Avalonia.Services;

public static class AppIconProvider
{
	private const string AppIconResource = "app-ico.ico";
	private const string TrayInactiveIconResource = "app-ico-inactive.ico";
	private const string TrayActiveIconResource = "app-ico-active.ico";

	private static WindowIcon? _appIcon;
	private static WindowIcon? _trayInactiveIcon;
	private static WindowIcon? _trayActiveIcon;

	public static WindowIcon? GetIcon()
	{
		return _appIcon ??= LoadIcon(AppIconResource);
	}

	public static WindowIcon? GetTrayIcon(bool isActive)
	{
		if (isActive)
		{
			return _trayActiveIcon ??= LoadIcon(TrayActiveIconResource);
		}

		return _trayInactiveIcon ??= LoadIcon(TrayInactiveIconResource);
	}

	private static WindowIcon? LoadIcon(string resourceName)
	{
		try
		{
			using var resourceStream = typeof(AppIconProvider).Assembly.GetManifestResourceStream(resourceName);
			if (resourceStream == null)
			{
				return null;
			}

			// Copy to MemoryStream so the manifest resource stream can be disposed
			var memoryStream = new MemoryStream();
			resourceStream.CopyTo(memoryStream);
			memoryStream.Position = 0;

			return new WindowIcon(memoryStream);
		}
		catch
		{
			return null;
		}
	}
}
