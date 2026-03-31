using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkTracker.WPF.Services;

public static class AppIconProvider
{
	private const string AppIconResource = "app-ico.ico";
	private const string TrayInactiveIconResource = "app-ico-inactive.ico";
	private const string TrayActiveIconResource = "app-ico-active.ico";

	private static ImageSource? _appIcon;
	private static Icon? _trayInactiveIcon;
	private static Icon? _trayActiveIcon;

	public static ImageSource? GetIcon()
	{
		if (_appIcon != null)
		{
			return _appIcon;
		}

		try
		{
			using var stream = typeof(AppIconProvider).Assembly.GetManifestResourceStream(AppIconResource);
			if (stream == null)
			{
				return null;
			}

			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = stream;
			image.EndInit();
			image.Freeze();

			_appIcon = image;
			return image;
		}
		catch
		{
			return null;
		}
	}

	public static Icon? GetTrayIcon(bool isActive)
	{
		if (isActive)
		{
			return _trayActiveIcon ??= LoadTrayIcon(TrayActiveIconResource);
		}

		return _trayInactiveIcon ??= LoadTrayIcon(TrayInactiveIconResource);
	}

	private static Icon? LoadTrayIcon(string resourceName)
	{
		try
		{
			using var resourceStream = typeof(AppIconProvider).Assembly.GetManifestResourceStream(resourceName);
			if (resourceStream == null)
			{
				return null;
			}

			// Icon(Stream) keeps the stream open, so copy to a MemoryStream first
			var memoryStream = new MemoryStream();
			resourceStream.CopyTo(memoryStream);
			memoryStream.Position = 0;

			return new Icon(memoryStream);
		}
		catch
		{
			return null;
		}
	}
}
