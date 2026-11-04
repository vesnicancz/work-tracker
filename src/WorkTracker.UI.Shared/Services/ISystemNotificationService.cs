namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Service for showing OS-level system notifications (toast / notification center).
/// Used for events that need to be visible even when the application window is minimized or
/// in the system tray, like Pomodoro phase transitions or the outcome of a remote command.
/// </summary>
public interface ISystemNotificationService
{
	Task ShowNotificationAsync(string title, string message);

	/// <summary>
	/// Shows a system notification with an optional action URL that opens in the browser when clicked.
	/// </summary>
	Task ShowNotificationAsync(string title, string message, string? actionUrl);

	/// <summary>
	/// Shows a system notification that takes the place of the previous one shown this way, for messages
	/// that are out of date once the next one arrives. Other notifications are left alone.
	/// </summary>
	Task ShowReplacingNotificationAsync(string title, string message);
}
