using System.Reflection;
using Avalonia.Controls;

namespace WorkTracker.Avalonia.Services;

public static class AppIconProvider
{
	private const string IdleIconResource = "app-ico.ico";
	private const string ActiveIconResource = "app-ico-active.ico";

	private static WindowIcon? _idleIcon;
	private static WindowIcon? _activeIcon;

	public static WindowIcon? GetIcon(bool isActive)
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

			var icon = new WindowIcon(stream);

			if (isActive)
			{
				_activeIcon = icon;
			}
			else
			{
				_idleIcon = icon;
			}

			return icon;
		}
		catch
		{
			return null;
		}
	}
}
