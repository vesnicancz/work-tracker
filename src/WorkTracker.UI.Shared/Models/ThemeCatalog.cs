namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Central registry of available theme names, split by light/dark variant.
/// Used by the settings UI (filter day/night dropdowns) and by the theme
/// switcher (determine FluentTheme variant for native controls).
/// </summary>
public static class ThemeCatalog
{
	public static readonly string[] LightThemes =
	[
		"Cobalt",
		"Coral",
		"Light",
		ApplicationSettings.DefaultTheme,
		"Sandstone"
	];

	public static readonly string[] DarkThemes =
	[
		"Abyss",
		"Dark",
		"Eclipse",
		"Midnight",
		"Purple",
		"Synthwave"
	];

	public static readonly string[] AllThemes = [.. LightThemes.Concat(DarkThemes).OrderBy(t => t)];

	public static readonly string[] LightThemesSorted = [.. LightThemes.OrderBy(t => t)];

	public static readonly string[] DarkThemesSorted = [.. DarkThemes.OrderBy(t => t)];

	public const string DefaultLightTheme = ApplicationSettings.DefaultTheme;
	public const string DefaultDarkTheme = "Dark";

	public static bool IsLight(string themeName) => Array.IndexOf(LightThemes, themeName) >= 0;
}
