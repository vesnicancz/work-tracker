namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Resolves application icon file paths for idle/active tracking states.
/// Platform-specific projects wrap the returned path into their native icon type.
/// </summary>
public static class AppIconResolver
{
	private const string IdleIcon = "app-ico.ico";
	private const string ActiveIcon = "app-ico-active.ico";

	/// <summary>
	/// Returns the full file path for the icon matching the given tracking state,
	/// or null if the file does not exist.
	/// </summary>
	public static string? GetIconPath(bool isActive, string iconDirectory)
	{
		var iconName = isActive ? ActiveIcon : IdleIcon;
		var path = Path.Combine(iconDirectory, iconName);
		return File.Exists(path) ? path : null;
	}
}
