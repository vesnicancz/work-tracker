using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Application settings model
/// </summary>
public class ApplicationSettings
{
	/// <summary>
	/// Last-selected submission mode in the Submit dialog. Persisted so the dialog
	/// remembers the user's preferred mode across sessions.
	/// </summary>
	public WorklogSubmissionMode LastSubmissionMode { get; set; } = WorklogSubmissionMode.Timed;

	/// <summary>
	/// Behavior when closing the main window
	/// </summary>
	public CloseWindowBehavior CloseWindowBehavior { get; set; } = CloseWindowBehavior.MinimizeToTray;

	/// <summary>
	/// Whether the application should start automatically with Windows
	/// </summary>
	public bool StartWithWindows { get; set; }

	/// <summary>
	/// Whether the application should start minimized to tray
	/// </summary>
	public bool StartMinimized { get; set; }

	/// <summary>
	/// Whether to check GitHub for newer releases on startup
	/// </summary>
	public bool CheckForUpdates { get; set; } = true;

	/// <summary>
	/// Plugin configurations (pluginId -> configuration dictionary)
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> PluginConfigurations { get; set; } = new();

	/// <summary>
	/// Enabled plugins (pluginId -> enabled state)
	/// </summary>
	public Dictionary<string, bool> EnabledPlugins { get; set; } = new();

	/// <summary>
	/// Favorite work items for quick access from tray menu
	/// </summary>
	public List<FavoriteWorkItem> FavoriteWorkItems { get; set; } = new();

	public const string DefaultTheme = "Modern Blue";

	/// <summary>
	/// Application theme used when <see cref="FollowSystemTheme"/> is false (e.g. "Modern Blue",
	/// "Dark", "Light", "Midnight", "Purple", "Abyss", "Cobalt", "Coral", "Eclipse",
	/// "Sandstone", "Synthwave").
	/// </summary>
	public string Theme { get; set; } = DefaultTheme;

	/// <summary>
	/// When true, the active theme is selected automatically based on the operating
	/// system's day/night setting. Falls back to <see cref="Theme"/> when false.
	/// </summary>
	public bool FollowSystemTheme { get; set; }

	/// <summary>
	/// Theme applied while <see cref="FollowSystemTheme"/> is true and the OS is in light mode.
	/// </summary>
	public string LightTheme { get; set; } = ThemeCatalog.DefaultLightTheme;

	/// <summary>
	/// Theme applied while <see cref="FollowSystemTheme"/> is true and the OS is in dark mode.
	/// </summary>
	public string DarkTheme { get; set; } = ThemeCatalog.DefaultDarkTheme;

	/// <summary>
	/// Pomodoro timer settings
	/// </summary>
	public PomodoroSettings Pomodoro { get; set; } = new();
}
