using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Adapts the static <see cref="ThemeManager"/> to <see cref="IThemeService"/>.
/// Startup applies the theme before the DI container exists and keeps calling the static directly;
/// everything running under DI goes through this.
/// </summary>
public class ThemeService : IThemeService
{
	public void ApplyThemeMode(bool followSystemTheme, string singleTheme, string lightTheme, string darkTheme)
		=> ThemeManager.ApplyThemeMode(followSystemTheme, singleTheme, lightTheme, darkTheme);
}
