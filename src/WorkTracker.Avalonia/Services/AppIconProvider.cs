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
			using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
			if (resourceStream == null)
			{
				return null;
			}

			// Copy to MemoryStream so the manifest resource stream can be disposed
			var memoryStream = new MemoryStream();
			resourceStream.CopyTo(memoryStream);
			memoryStream.Position = 0;

			var icon = new WindowIcon(memoryStream);

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
