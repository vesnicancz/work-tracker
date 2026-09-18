using WorkTracker.Application.Settings;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Application settings model.
/// <para>
/// Derives from <see cref="PluginSettings"/> so the plugin slice is declared once and stays
/// byte-compatible with what the CLI reads; the inherited properties serialize flat, exactly
/// as they did when they were declared here.
/// </para>
/// </summary>
public class ApplicationSettings : PluginSettings
{
	/// <summary>
	/// Last-selected submission mode in the Submit dialog. Persisted so the dialog
	/// remembers the user's preferred mode across sessions, and used as the fallback for a
	/// provider that has no entry in <see cref="SubmissionModeByProvider"/> yet.
	/// </summary>
	public WorklogSubmissionMode LastSubmissionMode { get; set; } = WorklogSubmissionMode.Timed;

	/// <summary>
	/// Plugin id of the upload provider last used in the Submit dialog, so the dialog reopens on
	/// the same provider. <c>null</c> when nothing has been submitted yet (or when the settings
	/// file predates this property), in which case the dialog falls back to
	/// <see cref="LastSubmissionMode"/> and the first compatible provider.
	/// </summary>
	public string? LastSubmissionProviderId { get; set; }

	/// <summary>
	/// Last-used submission mode per upload provider (pluginId -> mode). Selecting a provider in
	/// the Submit dialog restores the mode that was last used with it; providers missing here fall
	/// back to <see cref="LastSubmissionMode"/>, and an entry the provider no longer supports is
	/// ignored.
	/// </summary>
	public Dictionary<string, WorklogSubmissionMode> SubmissionModeByProvider { get; set; } = new();

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
	/// UI language: <see cref="LanguageCatalog.SystemLanguage"/> to follow the OS, or a shipped
	/// language code ("cs", "en"). Stored as a string rather than an enum so a hand-edited or
	/// future value can never fail deserialization and take every other setting down with it;
	/// unknown values are normalized back to the system language when read.
	/// </summary>
	public string Language { get; set; } = LanguageCatalog.SystemLanguage;

	/// <summary>
	/// Pomodoro timer settings
	/// </summary>
	public PomodoroSettings Pomodoro { get; set; } = new();

	/// <summary>
	/// Returns an independent copy of these settings.
	/// <para>
	/// Callers that persist a subset of the settings (the Settings dialog owns most, but not all,
	/// of them) start from this rather than from <c>new ApplicationSettings()</c>, so a property
	/// they do not know about keeps its stored value instead of silently resetting to its default.
	/// </para>
	/// <para>
	/// <b>Add every new property here.</b> <c>ApplicationSettingsCloneTests</c> fails the build if
	/// you forget.
	/// </para>
	/// </summary>
	public ApplicationSettings Clone() => new()
	{
		LastSubmissionMode = LastSubmissionMode,
		LastSubmissionProviderId = LastSubmissionProviderId,
		SubmissionModeByProvider = new Dictionary<string, WorklogSubmissionMode>(SubmissionModeByProvider),
		CloseWindowBehavior = CloseWindowBehavior,
		StartWithWindows = StartWithWindows,
		StartMinimized = StartMinimized,
		CheckForUpdates = CheckForUpdates,
		PluginConfigurations = PluginConfigurations.ToDictionary(
			pair => pair.Key,
			pair => new Dictionary<string, string>(pair.Value)),
		EnabledPlugins = new Dictionary<string, bool>(EnabledPlugins),
		FavoriteWorkItems = FavoriteWorkItems.Select(item => new FavoriteWorkItem
		{
			Id = item.Id,
			Name = item.Name,
			TicketId = item.TicketId,
			Description = item.Description,
			ShowAsTemplate = item.ShowAsTemplate
		}).ToList(),
		Theme = Theme,
		FollowSystemTheme = FollowSystemTheme,
		LightTheme = LightTheme,
		DarkTheme = DarkTheme,
		Language = Language,
		Pomodoro = new PomodoroSettings
		{
			Enabled = Pomodoro.Enabled,
			WorkMinutes = Pomodoro.WorkMinutes,
			ShortBreakMinutes = Pomodoro.ShortBreakMinutes,
			LongBreakMinutes = Pomodoro.LongBreakMinutes,
			PomodorosBeforeLongBreak = Pomodoro.PomodorosBeforeLongBreak,
			AutoStartWorkTracking = Pomodoro.AutoStartWorkTracking,
			AutoStopWorkTracking = Pomodoro.AutoStopWorkTracking
		}
	};
}
