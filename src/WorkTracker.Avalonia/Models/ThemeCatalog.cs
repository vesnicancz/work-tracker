namespace WorkTracker.Avalonia.Models;

/// <summary>
/// One theme: the name stored in the settings and shown in the UI, its resource file in
/// <c>Resources/Themes</c>, and whether it is a light one.
/// </summary>
public sealed record ThemeInfo(string Name, string FileName, bool IsLight);

/// <summary>
/// Central registry of the available themes. Adding a theme means a new file in
/// <c>Resources/Themes</c> and an entry here.
/// </summary>
public static class ThemeCatalog
{
	public const string DefaultLightTheme = ApplicationSettings.DefaultTheme;
	public const string DefaultDarkTheme = "Dark";

	public static readonly IReadOnlyList<ThemeInfo> Themes =
	[
		new("Cobalt", "Cobalt.axaml", IsLight: true),
		new("Coral", "Coral.axaml", IsLight: true),
		new("Light", "OneLightTheme.axaml", IsLight: true),
		new(ApplicationSettings.DefaultTheme, "ModernBlueTheme.axaml", IsLight: true),
		new("Sandstone", "Sandstone.axaml", IsLight: true),
		new("Abyss", "Abyss.axaml", IsLight: false),
		new(DefaultDarkTheme, "OneDarkTheme.axaml", IsLight: false),
		new("Eclipse", "Eclipse.axaml", IsLight: false),
		new("Midnight", "MidnightTheme.axaml", IsLight: false),
		new("Purple", "PurpleTheme.axaml", IsLight: false),
		new("Synthwave", "Synthwave.axaml", IsLight: false)
	];

	public static readonly string[] LightThemes = [.. Themes.Where(t => t.IsLight).Select(t => t.Name)];

	public static readonly string[] DarkThemes = [.. Themes.Where(t => !t.IsLight).Select(t => t.Name)];

	public static readonly string[] AllThemes = [.. Themes.Select(t => t.Name).OrderBy(name => name)];

	public static readonly string[] LightThemesSorted = [.. LightThemes.OrderBy(name => name)];

	public static readonly string[] DarkThemesSorted = [.. DarkThemes.OrderBy(name => name)];

	public static bool IsLight(string themeName) => Array.IndexOf(LightThemes, themeName) >= 0;

	/// <summary>
	/// Finds a theme by name. A name that isn't known any more, say from a hand-edited settings file
	/// or a removed theme, gets the default dark theme.
	/// </summary>
	public static ThemeInfo Find(string themeName) =>
		Themes.FirstOrDefault(t => t.Name == themeName) ?? Themes.First(t => t.Name == DefaultDarkTheme);
}
