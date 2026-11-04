using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Switches the application's theme. Static because startup applies the theme before the DI container
/// exists; everything running under DI goes through <see cref="IThemeService"/>.
/// </summary>
public static class ThemeManager
{
	private const string ThemesPath = "avares://WorkTracker.Avalonia/Resources/Themes/";

	// Active theme-mode state used to react to OS theme changes
	private static bool s_followSystemTheme;
	private static string s_singleTheme = ApplicationSettings.DefaultTheme;
	private static string s_lightTheme = ThemeCatalog.DefaultLightTheme;
	private static string s_darkTheme = ThemeCatalog.DefaultDarkTheme;
	private static bool s_systemListenerHooked;

	public static event EventHandler? ThemeChanged;

	/// <summary>
	/// Applies the current theme based on whether the user wants to follow the OS day/night
	/// setting or stick to a single named theme. Also wires up the system listener so OS changes
	/// propagate while in follow-system mode.
	/// </summary>
	public static void ApplyThemeMode(bool followSystemTheme, string singleTheme, string lightTheme, string darkTheme)
	{
		s_followSystemTheme = followSystemTheme;
		s_singleTheme = singleTheme;
		s_lightTheme = lightTheme;
		s_darkTheme = darkTheme;

		EnsureSystemThemeListener();
		SwitchTheme(ResolveEffectiveTheme());
	}

	/// <summary>
	/// Applies a single theme immediately.
	/// </summary>
	public static void SwitchTheme(string themeName)
	{
		var app = global::Avalonia.Application.Current!;
		if (app.Resources is not global::Avalonia.Controls.ResourceDictionary resources)
		{
			return;
		}

		// Remove the currently loaded theme dictionary, if any.
		// _Defaults.axaml is the contract fallback and stays loaded; only the active theme
		// is swapped out so its keys layer on top of (override) the defaults.
		var existing = resources.MergedDictionaries
			.OfType<ResourceInclude>()
			.FirstOrDefault(r =>
			{
				var src = r.Source?.ToString();
				return src != null
					&& src.Contains("/Themes/")
					&& !src.Contains("_Defaults.axaml");
			});
		if (existing != null)
		{
			resources.MergedDictionaries.Remove(existing);
		}

		var theme = ThemeCatalog.Find(themeName);
		var uri = new Uri(ThemesPath + theme.FileName);
		resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });

		// Keep Avalonia's built-in FluentTheme variant in sync for native controls
		app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

		ThemeChanged?.Invoke(app, EventArgs.Empty);
	}

	/// <summary>
	/// Subscribes to OS color-mode changes once. The handler re-applies the current theme
	/// mode, which picks light/dark when in follow-system mode and is a no-op otherwise.
	/// </summary>
	private static void EnsureSystemThemeListener()
	{
		if (s_systemListenerHooked)
		{
			return;
		}

		var platform = global::Avalonia.Application.Current?.PlatformSettings;
		if (platform == null)
		{
			return;
		}

		platform.ColorValuesChanged += (_, _) =>
		{
			if (!s_followSystemTheme)
			{
				return;
			}

			Dispatcher.UIThread.Post(() => SwitchTheme(ResolveEffectiveTheme()));
		};

		s_systemListenerHooked = true;
	}

	private static string ResolveEffectiveTheme()
	{
		if (!s_followSystemTheme)
		{
			return s_singleTheme;
		}

		var systemVariant = global::Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
		return systemVariant == global::Avalonia.Platform.PlatformThemeVariant.Light
			? s_lightTheme
			: s_darkTheme;
	}
}
