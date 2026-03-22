using Avalonia.Controls;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public static class AppIconProvider
{
	private static readonly string IconDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons");

	public static WindowIcon? GetIcon(bool isActive)
	{
		var path = AppIconResolver.GetIconPath(isActive, IconDirectory);
		return path != null ? new WindowIcon(path) : null;
	}
}