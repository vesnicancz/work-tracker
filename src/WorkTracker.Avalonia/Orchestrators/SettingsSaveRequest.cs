using WorkTracker.Avalonia.Models;

namespace WorkTracker.Avalonia.Orchestrators;

public class SettingsSaveRequest
{
	public CloseWindowBehavior CloseWindowBehavior { get; set; }
	public bool StartWithWindows { get; set; }
	public bool StartMinimized { get; set; }
	public bool CheckForUpdates { get; set; }
	public bool ShowRemoteControlNotifications { get; set; }
	public string? Theme { get; set; }
	public bool FollowSystemTheme { get; set; }
	public string? LightTheme { get; set; }
	public string? DarkTheme { get; set; }
	public string? Language { get; set; }
	public List<FavoriteWorkItem> FavoriteWorkItems { get; set; } = new();
	public List<PluginSettingsEntry> Plugins { get; set; } = new();
	public PomodoroSettings Pomodoro { get; set; } = new();
}
