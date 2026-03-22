using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

public static class AppIconProvider
{
	private static readonly string IconDirectory =
		System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

	private static ImageSource? _idleIcon;
	private static ImageSource? _activeIcon;

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

		var path = AppIconResolver.GetIconPath(isActive, IconDirectory);
		if (path == null)
		{
			return null;
		}

		try
		{
			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.UriSource = new Uri(path);
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
}