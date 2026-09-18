namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Applies the active theme. Deliberately mirrors <see cref="ILocalizationService.ApplyLanguage"/>:
/// the Settings dialog previews through it live and reverts through it when dismissed, so the
/// ViewModel never reaches for platform theme statics and the revert stays unit-testable.
/// </summary>
public interface IThemeService
{
	/// <summary>
	/// Applies the theme for the given mode - a single named theme, or the light/dark pair
	/// selected automatically from the OS day/night setting when <paramref name="followSystemTheme"/>
	/// is true.
	/// </summary>
	void ApplyThemeMode(bool followSystemTheme, string singleTheme, string lightTheme, string darkTheme);
}
