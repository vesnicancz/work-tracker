using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public class SettingsSaveRequest
{
	public CloseWindowBehavior CloseWindowBehavior { get; set; }
	public bool StartWithWindows { get; set; }
	public bool StartMinimized { get; set; }
	public bool CheckForUpdates { get; set; }
	public string? Theme { get; set; }
	public List<FavoriteWorkItem> FavoriteWorkItems { get; set; } = new();
	public List<PluginViewModel> Plugins { get; set; } = new();
	public PomodoroSettings Pomodoro { get; set; } = new();
}