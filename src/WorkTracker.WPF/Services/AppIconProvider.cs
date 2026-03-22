using System.Drawing;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkTracker.WPF.Services;

public static class AppIconProvider
{
	private const string IdleIconResource = "app-ico.ico";
	private const string ActiveIconResource = "app-ico-active.ico";

	private static ImageSource? _idleIcon;
	private static ImageSource? _activeIcon;
	private static Icon? _idleTrayIcon;
	private static Icon? _activeTrayIcon;

	public static ImageSource? GetIcon(bool isActive)
	{
		if (isActive && _activeIcon != null)
		{
			return _activeIcon;
		}

		if (!isActive && _idleIcon != null)
		{
			return _idleIcon;
		}

		var resourceName = isActive ? ActiveIconResource : IdleIconResource;

		try
		{
			var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
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

			if (isActive)
			{
				_activeIcon = image;
			}
			else
			{
				_idleIcon = image;
			}

			return image;
		}
		catch
		{
			return null;
		}
	}

	public static Icon? GetTrayIcon(bool isActive)
	{
		if (isActive && _activeTrayIcon != null)
		{
			return _activeTrayIcon;
		}

		if (!isActive && _idleTrayIcon != null)
		{
			return _idleTrayIcon;
		}

		var resourceName = isActive ? ActiveIconResource : IdleIconResource;

		try
		{
			var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			if (stream == null)
			{
				return null;
			}

			var icon = new Icon(stream);

			if (isActive)
			{
				_activeTrayIcon = icon;
			}
			else
			{
				_idleTrayIcon = icon;
			}

			return icon;
		}
		catch
		{
			return null;
		}
	}
}
