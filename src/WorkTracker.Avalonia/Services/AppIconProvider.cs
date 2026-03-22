using Avalonia.Controls;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

public static class AppIconProvider
{
	private static readonly string iconDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons");

	public static WindowIcon? GetIcon(bool isActive)
	{
		var path = AppIconResolver.GetIconPath(isActive, iconDirectory);
		return path != null ? new WindowIcon(path) : null;
	}
}