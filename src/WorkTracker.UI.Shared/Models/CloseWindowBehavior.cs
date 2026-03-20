namespace WorkTracker.UI.Shared.Models;

/// <summary>
/// Defines what happens when the main window is closed
/// </summary>
public enum CloseWindowBehavior
{
	/// <summary>
	/// Minimize to system tray
	/// </summary>
	MinimizeToTray,

	/// <summary>
	/// Exit the application
	/// </summary>
	ExitApplication
}
