using System.Text.Json;

namespace WorkTracker.Avalonia.Models;

/// <summary>
/// What has to be known before the first window and before the DI container exists, so that the
/// window shows up correctly themed and in the right language. Read directly from settings.json.
/// </summary>
public sealed record EarlySettings(string Theme, bool FollowSystemTheme, string LightTheme, string DarkTheme, bool StartMinimized, string Language)
{
	private static readonly EarlySettings Defaults = new(
		ApplicationSettings.DefaultTheme,
		false,
		ThemeCatalog.DefaultLightTheme,
		ThemeCatalog.DefaultDarkTheme,
		false,
		LanguageCatalog.SystemLanguage);

	/// <summary>
	/// Never throws: a missing or broken file gives the defaults.
	/// </summary>
	public static EarlySettings Read(string settingsPath)
	{
		try
		{
			if (!File.Exists(settingsPath))
			{
				return Defaults;
			}

			using var stream = File.OpenRead(settingsPath);
			using var doc = JsonDocument.Parse(stream);
			var root = doc.RootElement;

			var theme = root.TryGetProperty("Theme", out var t) ? t.GetString() ?? Defaults.Theme : Defaults.Theme;
			var followSystem = root.TryGetProperty("FollowSystemTheme", out var f) && f.GetBoolean();
			var lightTheme = root.TryGetProperty("LightTheme", out var lt) ? lt.GetString() ?? Defaults.LightTheme : Defaults.LightTheme;
			var darkTheme = root.TryGetProperty("DarkTheme", out var dt) ? dt.GetString() ?? Defaults.DarkTheme : Defaults.DarkTheme;
			var startMinimized = root.TryGetProperty("StartMinimized", out var s) && s.GetBoolean();
			// ValueKind guard: a non-string Language (hand-edited, or written by another tool) would
			// throw from GetString and drop this whole method into its catch, losing the theme too.
			var language = root.TryGetProperty("Language", out var lang) && lang.ValueKind == JsonValueKind.String
				? lang.GetString() ?? Defaults.Language
				: Defaults.Language;

			return new EarlySettings(theme, followSystem, lightTheme, darkTheme, startMinimized, language);
		}
		catch
		{
			return Defaults;
		}
	}
}
